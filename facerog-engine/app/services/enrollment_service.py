import logging
import threading
from dataclasses import dataclass
from datetime import UTC, datetime

from app.core.exceptions import InvalidImageError, NoGalleryEventError
from app.ml.embedder import Embedding
from app.ml.face_analyzer import FaceAnalyzer
from app.models.face_record import FaceRecord
from app.repositories.backend_gallery import GalleryPerson, GallerySource
from app.repositories.face_repository import FaceRepository
from app.repositories.local_gallery import LocalGalleryStore
from app.repositories.vector_index import InMemoryVectorIndex
from app.utils.image import decode_image

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class EnrollmentSummary:
    event_id: int
    person_count: int
    template_count: int
    skipped_people: tuple[str, ...]


class EnrollmentService:
    """Builds the gallery of known people from an event's registrants in the backend."""

    def __init__(
        self,
        analyzer: FaceAnalyzer,
        repository: FaceRepository,
        vector_index: InMemoryVectorIndex,
        gallery_source: GallerySource,
        local_store: LocalGalleryStore,
    ) -> None:
        self._analyzer = analyzer
        self._repository = repository
        self._vector_index = vector_index
        self._gallery_source = gallery_source
        self._local_store = local_store
        self._sync_lock = threading.Lock()
        self._event_id: int | None = None
        self._finished_events: set[int] = set()
        # person_id -> (photo, templates), so a re-sync only embeds new or changed photos.
        self._templates: dict[str, tuple[bytes, list[Embedding]]] = {}

    @property
    def event_id(self) -> int | None:
        """The event whose registrants make up the gallery; None until the first sync."""
        return self._event_id

    def sync_gallery(self, event_id: int | None = None) -> EnrollmentSummary:
        """Replace the gallery with the event's registrants. None re-syncs the current event.

        Opening another event first switches to its copy on disk, so recognition is ready before
        the backend answers. That copy stays in use if the backend can't be reached.
        """
        event_id = event_id if event_id is not None else self._event_id
        if event_id is None:
            raise NoGalleryEventError()
        with self._sync_lock:
            if event_id != self._event_id:
                self._restore_from_disk(event_id)
            people = self._gallery_source.fetch_people(event_id)
            templates: dict[str, tuple[bytes, list[Embedding]]] = {}
            records: list[FaceRecord] = []
            skipped: list[str] = []
            for person in people:
                embeddings = self._templates_for(person)
                if not embeddings:
                    skipped.append(person.name)
                    continue
                if event_id not in self._finished_events and not self._is_unchanged(person):
                    self._local_store.save(event_id, person, embeddings, self._analyzer.embedding_model)
                templates[person.person_id] = (person.photo, embeddings)
                records.append(self._record_for(person, len(embeddings)))
            self._apply_gallery(templates, records)
            self._local_store.remove_others(event_id, set(templates))
        summary = EnrollmentSummary(
            event_id, len(records), sum(r.template_count for r in records), tuple(skipped)
        )
        logger.info(
            "Gallery for event %d: %d people (%d templates, %d without a usable face)",
            event_id, summary.person_count, summary.template_count, len(skipped),
        )
        return summary

    def remove_finished_events(self) -> set[int]:
        """Delete the disk copy of events the backend says are over; returns the ones deleted.

        A TV still open on such an event keeps working from memory, but nothing is written back.
        Finished events are asked about again, so one extended by an admin is stored again.
        """
        with self._sync_lock:
            stored = self._local_store.stored_event_ids()
            to_check = stored | self._finished_events
            if not to_check:
                return set()
            self._finished_events = self._gallery_source.fetch_finished_events(to_check)
            removed = stored & self._finished_events
            for event_id in removed:
                self._local_store.remove_event(event_id)
        if removed:
            logger.info("Deleted the stored faces of finished events %s", sorted(removed))
        return removed

    def list_faces(self) -> list[FaceRecord]:
        return self._repository.list_records()

    def get_face(self, person_id: str) -> FaceRecord:
        return self._repository.get_record(person_id)

    def _restore_from_disk(self, event_id: int) -> None:
        stored = self._local_store.load(event_id, self._analyzer.embedding_model)
        self._apply_gallery(
            {s.person.person_id: (s.person.photo, s.embeddings) for s in stored},
            [self._record_for(s.person, len(s.embeddings)) for s in stored],
        )
        self._event_id = event_id
        logger.info("Restored %d people for event %d from disk", len(stored), event_id)

    def _apply_gallery(
        self, templates: dict[str, tuple[bytes, list[Embedding]]], records: list[FaceRecord]
    ) -> None:
        self._repository.replace_records(records)
        self._vector_index.replace({pid: embeddings for pid, (_, embeddings) in templates.items()})
        self._templates = templates

    def _is_unchanged(self, person: GalleryPerson) -> bool:
        cached = self._templates.get(person.person_id)
        return cached is not None and cached[0] == person.photo

    def _templates_for(self, person: GalleryPerson) -> list[Embedding]:
        if self._is_unchanged(person):
            return self._templates[person.person_id][1]
        try:
            image = decode_image(person.photo)
        except InvalidImageError:
            logger.warning("Photo of %s could not be decoded", person.person_id)
            return []
        faces = self._analyzer.detect_faces(image)
        if not faces:
            logger.warning("No face found in the photo of %s", person.person_id)
            return []
        largest = max(faces, key=lambda face: face.area)
        return self._analyzer.embed_enrollment_templates(image, largest)

    def _record_for(self, person: GalleryPerson, template_count: int) -> FaceRecord:
        return FaceRecord(
            person_id=person.person_id,
            name=person.name,
            photo=person.photo,
            photo_media_type=person.photo_media_type,
            template_count=template_count,
            embedding_model=self._analyzer.embedding_model,
            enrolled_at=datetime.now(UTC),
        )
