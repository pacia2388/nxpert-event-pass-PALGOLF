"""The engine's on-disk copy of each event's gallery, so a restart doesn't wait for the backend.

The backend stays the source of truth: every sync overwrites changed people and removes anyone it
no longer lists. These files are biometric data and are git-ignored.

Layout (one folder per person, per event):

    datasets/faces/
        event_2/
            u15/
                info.json       name, photo media type and the embedding model
                profile.jpg     the registered picture
                templates.npy   the photo's embeddings, so it isn't embedded again
"""

import json
import logging
import re
import shutil
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from app.ml.embedder import Embedding
from app.repositories.backend_gallery import GalleryPerson

logger = logging.getLogger(__name__)

INFO_FILE_NAME = "info.json"
TEMPLATES_FILE_NAME = "templates.npy"
PROFILE_STEM = "profile"
PHOTO_EXTENSIONS = {"image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp"}
DEFAULT_PHOTO_EXTENSION = ".img"
# Person ids become folder names, so anything else from the backend is kept in memory only.
SAFE_PERSON_ID = re.compile(r"^[A-Za-z0-9_-]{1,128}$")
EVENT_FOLDER = re.compile(r"event_(\d+)")


@dataclass(frozen=True)
class StoredPerson:
    person: GalleryPerson
    embeddings: list[Embedding]


class LocalGalleryStore:
    """Reads and writes the copy. A disk problem is logged, never raised: recognition goes on."""

    def __init__(self, root: Path) -> None:
        self._root = root

    def load(self, event_id: int, embedding_model: str) -> list[StoredPerson]:
        """Everyone stored for the event. Unreadable folders and other models' templates are skipped."""
        event_dir = self._event_dir(event_id)
        if not event_dir.is_dir():
            return []
        people = []
        for folder in sorted(event_dir.iterdir()):
            stored = self._read_person(folder, embedding_model) if folder.is_dir() else None
            if stored is not None:
                people.append(stored)
        return people

    def save(
        self, event_id: int, person: GalleryPerson, embeddings: list[Embedding], embedding_model: str
    ) -> None:
        if not SAFE_PERSON_ID.fullmatch(person.person_id):
            logger.warning("Not storing a registrant whose id can't be a folder name")
            return
        folder = self._event_dir(event_id) / person.person_id
        info_path = folder / INFO_FILE_NAME
        try:
            folder.mkdir(parents=True, exist_ok=True)
            # info.json goes first and comes back last: a save cut short leaves a folder that is
            # skipped on load, never a new photo paired with the old templates.
            info_path.unlink(missing_ok=True)
            for old_photo in folder.glob(f"{PROFILE_STEM}.*"):
                old_photo.unlink()
            extension = PHOTO_EXTENSIONS.get(person.photo_media_type, DEFAULT_PHOTO_EXTENSION)
            photo_name = f"{PROFILE_STEM}{extension}"
            (folder / photo_name).write_bytes(person.photo)
            np.save(folder / TEMPLATES_FILE_NAME, np.stack(embeddings), allow_pickle=False)
            info_path.write_text(
                json.dumps(
                    {
                        "name": person.name,
                        "photo_file": photo_name,
                        "photo_media_type": person.photo_media_type,
                        "embedding_model": embedding_model,
                    },
                    indent=2,
                ),
                encoding="utf-8",
            )
        except OSError as err:
            logger.warning("Could not store %s on disk: %s", person.person_id, err)

    def remove_others(self, event_id: int, keep_person_ids: set[str]) -> None:
        """Delete everyone the backend no longer lists for the event."""
        event_dir = self._event_dir(event_id)
        if not event_dir.is_dir():
            return
        for folder in event_dir.iterdir():
            if folder.is_dir() and folder.name not in keep_person_ids:
                try:
                    shutil.rmtree(folder)
                except OSError as err:
                    logger.warning("Could not delete %s from disk: %s", folder.name, err)

    def stored_event_ids(self) -> set[int]:
        if not self._root.is_dir():
            return set()
        matches = (EVENT_FOLDER.fullmatch(folder.name) for folder in self._root.iterdir() if folder.is_dir())
        return {int(match[1]) for match in matches if match}

    def remove_event(self, event_id: int) -> None:
        try:
            shutil.rmtree(self._event_dir(event_id))
        except FileNotFoundError:
            pass
        except OSError as err:
            logger.warning("Could not delete event %d from disk: %s", event_id, err)

    def _event_dir(self, event_id: int) -> Path:
        return self._root / f"event_{event_id}"

    def _read_person(self, folder: Path, embedding_model: str) -> StoredPerson | None:
        try:
            info = json.loads((folder / INFO_FILE_NAME).read_text(encoding="utf-8"))
            if info["embedding_model"] != embedding_model:
                return None
            matrix = np.load(folder / TEMPLATES_FILE_NAME, allow_pickle=False)
            photo = (folder / info["photo_file"]).read_bytes()
            person = GalleryPerson(folder.name, info["name"], photo, info["photo_media_type"])
        except FileNotFoundError:
            return None  # a save that was cut short
        except (OSError, ValueError, KeyError, TypeError) as err:
            logger.warning("Ignoring the stored copy of %s: %s", folder.name, err)
            return None
        if matrix.ndim != 2 or matrix.shape[0] == 0:
            return None
        return StoredPerson(person, list(matrix))
