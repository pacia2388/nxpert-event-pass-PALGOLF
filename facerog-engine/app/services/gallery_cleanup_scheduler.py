import logging
import threading
from collections.abc import Callable

logger = logging.getLogger(__name__)

STOP_TIMEOUT_SECONDS = 5.0


class GalleryCleanupScheduler:
    """Runs the cleanup at startup and then on every interval, on a background thread."""

    def __init__(self, cleanup: Callable[[], object], interval_seconds: float) -> None:
        self._cleanup = cleanup
        self._interval_seconds = interval_seconds
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, name="gallery-cleanup", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread:
            self._thread.join(timeout=STOP_TIMEOUT_SECONDS)
        self._thread = None

    def _run(self) -> None:
        while not self._stop_event.is_set():
            try:
                self._cleanup()
            except Exception as err:  # noqa: BLE001 - one failed run must not end the schedule.
                logger.warning("Gallery cleanup skipped, nothing was deleted: %s", err)
            self._stop_event.wait(self._interval_seconds)
