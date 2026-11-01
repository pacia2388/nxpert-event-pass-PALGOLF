from pathlib import Path

import numpy as np

from app.repositories.backend_gallery import GalleryPerson
from app.repositories.local_gallery import LocalGalleryStore

MODEL = "w600k_r50"
PERSON = GalleryPerson("u1", "Juan Dela Cruz", b"photo-bytes", "image/jpeg")
EMBEDDINGS = [np.array([1.0, 0.0], dtype=np.float32), np.array([0.0, 1.0], dtype=np.float32)]


def test_save_then_load_returns_the_person_and_templates(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)

    [stored] = store.load(2, MODEL)

    assert stored.person == PERSON
    assert np.array_equal(np.stack(stored.embeddings), np.stack(EMBEDDINGS))
    assert (tmp_path / "event_2" / "u1" / "profile.jpg").read_bytes() == b"photo-bytes"


def test_events_are_kept_apart(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)

    assert store.load(3, MODEL) == []


def test_templates_of_another_model_are_ignored(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)

    assert store.load(2, "another_model") == []


def test_save_cut_short_is_skipped_on_load(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)
    (tmp_path / "event_2" / "u1" / "info.json").unlink()

    assert store.load(2, MODEL) == []


def test_new_photo_replaces_the_old_one(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)
    store.save(2, GalleryPerson("u1", "Juan Dela Cruz", b"png-bytes", "image/png"), EMBEDDINGS, MODEL)

    folder = tmp_path / "event_2" / "u1"
    assert [p.name for p in folder.glob("profile.*")] == ["profile.png"]


def test_remove_others_deletes_people_no_longer_listed(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)
    store.save(2, GalleryPerson("g-token", "Guest", b"x", "image/jpeg"), EMBEDDINGS, MODEL)

    store.remove_others(2, {"g-token"})

    assert [s.person.person_id for s in store.load(2, MODEL)] == ["g-token"]


def test_id_that_cant_be_a_folder_name_is_not_stored(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, GalleryPerson("../evil", "Evil", b"x", "image/jpeg"), EMBEDDINGS, MODEL)

    assert not (tmp_path / "evil").exists()
    assert store.load(2, MODEL) == []


def test_remove_event_deletes_only_that_event(tmp_path: Path) -> None:
    store = LocalGalleryStore(tmp_path)
    store.save(2, PERSON, EMBEDDINGS, MODEL)
    store.save(3, PERSON, EMBEDDINGS, MODEL)
    (tmp_path / "not_an_event").mkdir()

    store.remove_event(2)
    store.remove_event(99)

    assert store.stored_event_ids() == {3}
    assert (tmp_path / "not_an_event").is_dir()
