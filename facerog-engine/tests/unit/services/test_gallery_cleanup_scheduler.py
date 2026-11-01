import threading

from app.services.gallery_cleanup_scheduler import GalleryCleanupScheduler

WAIT_SECONDS = 5.0


def test_runs_at_start_and_again_after_a_failed_run() -> None:
    second_run = threading.Event()
    calls = []

    def cleanup() -> None:
        calls.append(1)
        if len(calls) == 1:
            raise RuntimeError("backend down")
        second_run.set()

    scheduler = GalleryCleanupScheduler(cleanup, interval_seconds=0.01)
    scheduler.start()
    try:
        assert second_run.wait(WAIT_SECONDS)
    finally:
        scheduler.stop()
