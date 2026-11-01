import pytest

from app.api.dependencies import ServiceContainer, wire_services
from app.core.config import Settings
from app.core.exceptions import BackendUnavailableError, NoGalleryEventError
from app.ml.face_analyzer import FaceAnalyzer
from app.repositories.backend_gallery import GalleryPerson
from tests.conftest import FakeGallerySource, gallery_person


def test_sync_gallery_builds_four_templates_per_photo(container: ServiceContainer) -> None:
    summary = container.enrollment_service.sync_gallery(1)
    assert (summary.event_id, summary.person_count, summary.template_count) == (1, 1, 4)
    record = container.enrollment_service.get_face("u1")
    assert record.name == "Red Person"
    assert record.photo_media_type == "image/png"


def test_sync_without_event_raises(container: ServiceContainer) -> None:
    with pytest.raises(NoGalleryEventError):
        container.enrollment_service.sync_gallery()


def test_resync_replaces_gallery_with_current_registrants(
    container: ServiceContainer, gallery_source: FakeGallerySource
) -> None:
    service = container.enrollment_service
    service.sync_gallery(1)
    gallery_source.people_by_event[1] = [gallery_person("g-token", "blue")]

    summary = service.sync_gallery()

    assert summary.event_id == 1
    assert [face.person_id for face in service.list_faces()] == ["g-token"]


def test_photo_without_face_is_skipped(
    container: ServiceContainer, gallery_source: FakeGallerySource
) -> None:
    gallery_source.people_by_event[2] = [GalleryPerson("u9", "Nobody", b"not an image", "image/png")]

    summary = container.enrollment_service.sync_gallery(2)

    assert summary.person_count == 0
    assert summary.skipped_people == ("Nobody",)


class _UnreachableBackend:
    def fetch_people(self, event_id: int) -> list[GalleryPerson]:
        raise BackendUnavailableError("down")

    def fetch_finished_events(self, event_ids: set[int]) -> set[int]:
        raise BackendUnavailableError("down")


def test_restart_recognizes_from_disk_while_backend_is_down(
    container: ServiceContainer, settings: Settings, face_analyzer: FaceAnalyzer
) -> None:
    container.enrollment_service.sync_gallery(1)
    restarted = wire_services(settings, face_analyzer, face_analyzer, _UnreachableBackend())

    with pytest.raises(BackendUnavailableError):
        restarted.enrollment_service.sync_gallery(1)

    assert [face.person_id for face in restarted.enrollment_service.list_faces()] == ["u1"]


def test_unchanged_photo_is_not_embedded_again_after_restart(
    container: ServiceContainer,
    settings: Settings,
    face_analyzer: FaceAnalyzer,
    gallery_source: FakeGallerySource,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    container.enrollment_service.sync_gallery(1)
    restarted = wire_services(settings, face_analyzer, face_analyzer, gallery_source)
    monkeypatch.setattr(face_analyzer, "embed_enrollment_templates", lambda *_: pytest.fail("embedded again"))

    summary = restarted.enrollment_service.sync_gallery(1)

    assert summary.person_count == 1


def test_person_removed_in_backend_is_deleted_from_disk(
    container: ServiceContainer, settings: Settings, gallery_source: FakeGallerySource
) -> None:
    service = container.enrollment_service
    service.sync_gallery(1)
    assert (settings.dataset_dir / "event_1" / "u1").is_dir()
    gallery_source.people_by_event[1] = []

    service.sync_gallery()

    assert not (settings.dataset_dir / "event_1" / "u1").exists()


def test_opening_another_event_does_not_keep_the_previous_people(
    container: ServiceContainer, gallery_source: FakeGallerySource
) -> None:
    service = container.enrollment_service
    service.sync_gallery(1)
    gallery_source.people_by_event[2] = []

    service.sync_gallery(2)

    assert service.list_faces() == []


def test_finished_event_is_deleted_and_running_event_is_kept(
    container: ServiceContainer, settings: Settings, gallery_source: FakeGallerySource
) -> None:
    service = container.enrollment_service
    gallery_source.people_by_event[2] = [gallery_person("g-token", "blue")]
    service.sync_gallery(1)
    service.sync_gallery(2)
    gallery_source.finished_events = {1}

    assert service.remove_finished_events() == {1}

    assert not (settings.dataset_dir / "event_1").exists()
    assert (settings.dataset_dir / "event_2" / "g-token").is_dir()


def test_nothing_is_deleted_when_backend_is_down(
    container: ServiceContainer, settings: Settings, face_analyzer: FaceAnalyzer
) -> None:
    container.enrollment_service.sync_gallery(1)
    restarted = wire_services(settings, face_analyzer, face_analyzer, _UnreachableBackend())

    with pytest.raises(BackendUnavailableError):
        restarted.enrollment_service.remove_finished_events()

    assert (settings.dataset_dir / "event_1" / "u1").is_dir()


def test_finished_event_is_not_written_back_but_is_stored_again_once_extended(
    container: ServiceContainer, settings: Settings, gallery_source: FakeGallerySource, face_analyzer: FaceAnalyzer
) -> None:
    service = container.enrollment_service
    service.sync_gallery(1)
    gallery_source.finished_events = {1}
    service.remove_finished_events()
    gallery_source.people_by_event[1] = [gallery_person("u2", "blue")]

    service.sync_gallery(1)
    assert [face.person_id for face in service.list_faces()] == ["u2"]
    assert not (settings.dataset_dir / "event_1").exists()

    gallery_source.finished_events = set()
    service.remove_finished_events()
    gallery_source.people_by_event[1] = [gallery_person("u3", "green")]
    service.sync_gallery(1)
    assert (settings.dataset_dir / "event_1" / "u3").is_dir()
