import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime, filter, firstValueFrom, timeout } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { CAMERA_FAILURE_MESSAGES, CameraFailure, cameraFailure } from '../../core/camera-failure';
import {
  EngineCheckIn,
  EngineDetectionMode,
  EngineScanMode,
  EngineSnapshot,
  EngineTrack,
  FaceEngineService,
} from '../../core/face-engine.service';
import { AttendeeDto, CheckInResult } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';
import { TvBanner, TvBannerService } from '../../core/tv-banner.service';

const POLL_MS = 250;
// The gallery re-syncs as soon as the backend reports a change (registration added/removed,
// photo changed). Changes that arrive together (a bulk import) are batched into one reload.
const GALLERY_CHANGE_DEBOUNCE_MS = 2000;
// Safety net for pushes the TV missed (hub dropped, change made outside the API).
const GALLERY_SYNC_MS = 5 * 60 * 1000;
// On first load only check-ins from the last few seconds are shown, so a refresh doesn't replay
// the whole session but someone who just arrived is still greeted.
const REPLAY_RECENT_SECONDS = 30;
const QUEUE_SLOTS = 4;
// The attendee list also refreshes on its own, to pick up QR and manual check-ins.
const ATTENDEES_REFRESH_MS = 30 * 1000;
const CAMERA_RETRY_MS = 3000;
// An engine that accepts connections but never answers counts as offline.
const ENGINE_TIMEOUT_MS = 10 * 1000;
// Frames go to the engine at its own working width (FRE_VIDEO_FRAME_WIDTH), so it sees the same
// detail as from a local camera and no bandwidth is spent on pixels it would discard.
const FRAME_WIDTH = 960;
// Below this, small faces (far from the camera) start to lose recognition similarity.
const FRAME_JPEG_QUALITY = 0.92;
// A second frame is already on its way when the engine finishes one, so it doesn't sit idle while
// answers travel over the network. More would only make the frames older.
const FRAMES_IN_FLIGHT = 2;
const FRAME_MIN_INTERVAL_MS = 100;
const FRAME_TIMEOUT_MS = 10 * 1000;
// A check-in the backend never answers ends as an error, so it can't hold up the queue.
const CHECK_IN_TIMEOUT_MS = 10 * 1000;
const FRAME_STATS_SMOOTHING = 0.9;
// Zooms of angled faces inside the camera preview, like the insets on the engine's own video.
const MAX_ZOOMS = 4;
const ZOOM_REFRESH_MS = 250;
const BOX_EASE_MAX_MS = 250;
// A face is announced as not registered only after a fair look, so a registered person who just
// walked in, or something in the background that resembles a face, doesn't get flagged.
const NOT_REGISTERED_MIN_ATTEMPTS = 3;
const NOT_REGISTERED_MIN_SECONDS = 1.5;
const NOT_REGISTERED_MIN_FACE_WIDTH = 0.08; // of the frame's width
const NOT_REGISTERED_PHOTO_SIZE = 320;
const NOT_REGISTERED_PHOTO_MARGIN = 0.25;
// An announced face shows again only after its box has been gone this many engine frames (about a
// second). Counted in frames so a slow engine or a detector blink doesn't re-announce someone
// who never left.
const NOT_REGISTERED_GONE_FRAMES = 10;
// Exit waits this long at most for the engine to stop before leaving.
const EXIT_RELEASE_TIMEOUT_MS = 8000;
// The TV's scan mode is remembered per device.
const SCAN_MODE_KEY = 'nxpass.tvScanMode';
const SCAN_MODES: EngineScanMode[] = ['qr', 'face', 'auto'];
// So is the face detection mode: wait until people are steady, or detect continuously.
const DETECTION_MODE_KEY = 'nxpass.tvDetectionMode';
const DETECTION_MODES: EngineDetectionMode[] = ['steady', 'continuous'];
// The camera preview's size is remembered per device too; null means the default size.
const CAMERA_WIDTH_KEY = 'nxpass.tvCameraWidth';
const CAMERA_MIN_WIDTH = 120;
const CAMERA_ASPECT = 16 / 9;
const CAMERA_KEY_STEP = 24;
// How a banner fills the screen, remembered per device: cover (no borders, may crop), contain
// (whole image over a blurred copy of itself) or fill (stretched to the screen).
type BannerFit = 'cover' | 'contain' | 'fill';
const BANNER_FIT_KEY = 'nxpass.tvBannerFit';
const BANNER_FITS: BannerFit[] = ['cover', 'contain', 'fill'];
const BANNER_NOTICE_MS = 6000;

function loadBannerFit(): BannerFit {
  try {
    const v = localStorage.getItem(BANNER_FIT_KEY) as BannerFit | null;
    return v && BANNER_FITS.includes(v) ? v : 'cover';
  } catch {
    return 'cover';
  }
}

function loadCameraWidth(): number | null {
  try {
    const v = Number(localStorage.getItem(CAMERA_WIDTH_KEY));
    return v >= CAMERA_MIN_WIDTH ? v : null;
  } catch {
    return null;
  }
}

// Up to 60% of the screen in either direction, so the preview never covers the spotlight.
function clampCameraWidth(width: number): number {
  const max = Math.min(window.innerWidth * 0.6, window.innerHeight * 0.6 * CAMERA_ASPECT);
  return Math.round(Math.max(CAMERA_MIN_WIDTH, Math.min(width, max)));
}

function loadScanMode(): EngineScanMode {
  try {
    const v = localStorage.getItem(SCAN_MODE_KEY) as EngineScanMode | null;
    return v && SCAN_MODES.includes(v) ? v : 'face';
  } catch {
    return 'face';
  }
}

function loadDetectionMode(): EngineDetectionMode {
  try {
    const v = localStorage.getItem(DETECTION_MODE_KEY) as EngineDetectionMode | null;
    return v && DETECTION_MODES.includes(v) ? v : 'steady';
  } catch {
    return 'steady';
  }
}

// A face box over the camera preview, in percent of the preview.
interface FaceBox {
  id: number;
  left: number;
  top: number;
  width: number;
  height: number;
  status: EngineTrack['status'];
}

function smoothed(previous: number | null, value: number): number {
  return previous === null ? value : FRAME_STATS_SMOOTHING * previous + (1 - FRAME_STATS_SMOOTHING) * value;
}

// The preview is mirrored like a selfie camera, so the boxes are too; the frames stay unmirrored.
function faceBox(track: EngineTrack, snapshot: EngineSnapshot): FaceBox {
  const { x1, y1, x2, y2 } = track.bbox;
  const { frame_width: w, frame_height: h } = snapshot;
  return {
    id: track.track_id,
    left: (1 - x2 / w) * 100,
    top: (y1 / h) * 100,
    width: ((x2 - x1) / w) * 100,
    height: ((y2 - y1) / h) * 100,
    status: track.status,
  };
}

function isNotRegistered(track: EngineTrack, frameWidth: number): boolean {
  return (
    track.status === 'unknown' &&
    track.recognition_count >= NOT_REGISTERED_MIN_ATTEMPTS &&
    track.seconds_tracked >= NOT_REGISTERED_MIN_SECONDS &&
    (track.bbox.x2 - track.bbox.x1) / frameWidth >= NOT_REGISTERED_MIN_FACE_WIDTH
  );
}

// The backend's answer, or 'notregistered': a face that matches no registrant, which the TV
// announces on its own.
type TvResult = Omit<CheckInResult, 'status'> & { status: CheckInResult['status'] | 'notregistered' };

// One person on the TV. Someone recognized is shown as soon as the engine reports them; result is
// null until the backend has recorded (or refused) the check-in.
interface TvItem {
  sequence: number;
  name: string;
  photo: string;
  result: TvResult | null;
}

/**
 * Door TV for camera check-in. This browser's webcam sends frames to the face engine, which
 * reports who it recognized; each person is checked in through the backend, and the backend's
 * answer is what the TV shows (so only confirmed registrations, inside the event window, are
 * checked in).
 */
@Component({
  selector: 'app-face-tv',
  templateUrl: './face-tv.html',
  styleUrl: './face-tv.scss',
})
export class FaceTvComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private api = inject(ApiService);
  private engine = inject(FaceEngineService);
  private realtime = inject(RealtimeService);
  private tvBanners = inject(TvBannerService);

  readonly eventId = Number(this.route.snapshot.paramMap.get('eventId'));
  readonly cameraStream = signal<MediaStream | null>(null);
  readonly cameraProblem = signal<string | null>(null);
  // Width / height of the camera's picture, so the preview and the boxes line up with it.
  readonly cameraAspect = signal<number | null>(null);
  readonly faceBoxes = signal<FaceBox[]>([]);
  readonly faceZooms = signal<{ id: number; url: string }[]>([]);
  // Smoothed time from sending a frame to the engine's answer (upload + recognition + download).
  readonly frameRoundTripMs = signal<number | null>(null);
  readonly framesPerSecond = signal(0);
  readonly frameStats = computed(() => {
    const ms = this.frameRoundTripMs();
    return ms === null
      ? 'Connecting to the face engine…'
      : `Recognizing ${this.framesPerSecond().toFixed(1)} frames/s · answers in ${Math.round(ms)} ms`;
  });
  // Each box glides for as long as the next answer takes, so it keeps moving between answers.
  readonly boxEaseMs = computed(() => {
    const fps = this.framesPerSecond();
    return fps > 0 ? Math.min(BOX_EASE_MAX_MS, 1000 / fps) : BOX_EASE_MAX_MS;
  });
  readonly engineOnline = signal(true);
  readonly exiting = signal(false);
  readonly galleryMessage = signal('');
  readonly galleryError = signal(false);

  // The event's own branding (set on the event form): banner image and TV welcome message.
  readonly eventTitle = signal('');
  readonly eventWelcome = signal<string | null>(null);
  readonly eventBanner = signal<string | null>(null);
  // Verify-only mode (stored on the event): the TV identifies people but nobody is checked in.
  readonly verifyOnly = signal(false);

  readonly current = signal<TvItem | null>(null);
  readonly shown = computed(() => (this.current() ? [this.current()!] : []));
  readonly waiting = signal<TvItem[]>([]);
  readonly queuePreview = computed(() => this.waiting().slice(0, QUEUE_SLOTS));
  readonly queueOverflow = computed(() => Math.max(0, this.waiting().length - QUEUE_SLOTS));
  readonly displaySeconds = signal(3);

  // What the camera checks people in with: QR passes, faces, or both (auto).
  readonly scanMode = signal<EngineScanMode>(loadScanMode());
  readonly scanModes: { value: EngineScanMode; label: string }[] = [
    { value: 'qr', label: 'QR' },
    { value: 'face', label: 'Face' },
    { value: 'auto', label: 'Auto' },
  ];
  readonly idleTitle = computed(() =>
    ({
      qr: 'Show your QR pass to the camera',
      face: 'Look at the camera to check in',
      auto: 'Look at the camera or show your QR pass',
    })[this.scanMode()],
  );

  // When faces are identified: once the person stands or moves a little (steady), or right
  // away even while walking quickly (continuous). Only matters when faces are scanned.
  readonly detectionMode = signal<EngineDetectionMode>(loadDetectionMode());
  readonly detectionModes: { value: EngineDetectionMode; label: string; hint: string }[] = [
    { value: 'steady', label: 'Steady', hint: 'Detect once the person stands still or moves a little' },
    { value: 'continuous', label: 'Continuous', hint: 'Detect right away, even people walking quickly' },
  ];
  readonly idleHint = computed(() =>
    this.scanMode() !== 'qr' && this.detectionMode() === 'steady'
      ? 'Stand still for a moment in front of the camera.'
      : 'Registered participants appear here automatically.',
  );

  // Width of the camera preview in px, set by dragging its corner; null uses the CSS default.
  readonly cameraWidth = signal<number | null>(loadCameraWidth());

  // The attendee sidebar can be closed so the stage takes the full width.
  readonly sideOpen = signal(true);

  // Camera off pauses check-in (the browser releases the webcam) and shows the standby banner:
  // the banner uploaded on this device, else the event's saved banner. Turning it back on
  // returns to the normal check-in screen.
  readonly cameraOn = signal(true);
  readonly cameraSwitching = signal(false);
  readonly bannerFit = signal<BannerFit>(loadBannerFit());
  readonly bannerFits: { value: BannerFit; label: string; hint: string }[] = [
    { value: 'cover', label: 'Fill screen', hint: 'Covers the whole screen without borders; edges may be cropped' },
    { value: 'contain', label: 'Fit whole image', hint: 'Shows the whole image over a blurred copy of itself' },
    { value: 'fill', label: 'Stretch', hint: 'Fills the screen and shows everything, but may distort the image' },
  ];
  // Custom TV banner, saved per event in this browser (IndexedDB).
  readonly customBanner = signal<TvBanner | null>(null);
  readonly bannerBusy = signal(false);
  readonly bannerNotice = signal<string | null>(null);
  readonly canEditBanner = computed(() => !this.cameraOn() && !this.cameraSwitching() && !this.bannerBusy());
  readonly standbyImage = computed(() => this.customBanner()?.image ?? this.eventBanner());

  // Right-side list: every registration of the event, latest check-in first, then the rest by name.
  readonly attendees = signal<AttendeeDto[]>([]);
  readonly checkedInCount = computed(() => this.attendees().filter((a) => a.checkedInAt).length);
  readonly attendeeRows = computed(() =>
    [...this.attendees()]
      .sort((a, b) => {
        if (a.checkedInAt && b.checkedInAt) return b.checkedInAt.localeCompare(a.checkedInAt);
        if (a.checkedInAt || b.checkedInAt) return a.checkedInAt ? -1 : 1;
        return a.fullName.localeCompare(b.fullName);
      })
      .map((a) => ({
        id: a.registrationId,
        name: a.fullName,
        detail: a.sessionTitle,
        ...(a.checkedInAt
          ? { tone: 'good', label: `✓ ${this.formatTime(new Date(a.checkedInAt))}` }
          : a.confirmedAt
            ? { tone: 'muted', label: 'Not yet' }
            : { tone: 'warn', label: 'Pending' }),
      })),
  );

  // How the spotlight presents the backend's answer. The tone picks the full-screen colour:
  // green for a new check-in, amber for already in / not confirmed, yellow for a face that isn't
  // registered, grey outside the check-in window, red otherwise. Member details (chips, event)
  // only show for valid / already.
  readonly spotlight = computed(() => {
    const item = this.current();
    if (!item) return null;
    const r = item.result;
    // Nothing is promised until the backend answers.
    if (!r) return { tone: 'info', kicker: null, badge: 'Checking…', note: null, chips: [], event: null, session: null };
    const at = r.checkedInAt ? `at ${this.formatTime(new Date(r.checkedInAt))}` : null;
    const chips = [r.level, r.senateNo ? `Senate No. ${r.senateNo}` : null, r.organization].filter(
      (c): c is string => !!c,
    );
    const details = { chips, event: r.eventTitle, session: r.sessionTitle };
    const none = { chips: [] as string[], event: null, session: null };
    switch (r.status) {
      case 'valid':
        return {
          tone: 'good',
          kicker: r.welcomeMessage || this.eventWelcome() || 'Welcome',
          badge: '✓ Checked in',
          note: at,
          ...details,
        };
      case 'verified':
        return {
          tone: 'info',
          kicker: r.welcomeMessage || this.eventWelcome() || 'Welcome',
          badge: '✓ Recognized — Verify only',
          note: 'Confirmed registration — not checked in.',
          ...details,
        };
      case 'already':
        return { tone: 'warn', kicker: 'Welcome back', badge: 'Already checked in', note: at, ...details };
      case 'unconfirmed':
        return {
          tone: 'warn',
          kicker: null,
          badge: 'Registration not yet confirmed',
          note: 'Please see the registration desk.',
          ...none,
        };
      case 'notregistered':
        return {
          tone: 'notice',
          kicker: null,
          badge: 'Please see the registration desk',
          note: null,
          ...none,
        };
      case 'notstarted':
      case 'ended':
        return {
          tone: 'muted',
          kicker: null,
          badge: r.status === 'ended' ? 'Check-in closed' : 'Check-in not open yet',
          note: r.message,
          ...none,
        };
      default:
        return {
          tone: 'bad',
          kicker: null,
          badge: 'Not checked in',
          note: r.message || 'Please see the registration desk.',
          ...none,
        };
    }
  });

  // Shown in place of the photo when there is none.
  initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join('');
  }

  private sessionId: string | null = null;
  private lastSequence = 0;
  private isPolling = false;
  private timers: ReturnType<typeof setInterval>[] = [];
  private showTimer: ReturnType<typeof setTimeout> | null = null;
  private cameraRetry: ReturnType<typeof setTimeout> | null = null;
  @ViewChild('cameraVideo') private cameraVideo?: ElementRef<HTMLVideoElement>;
  private frameCanvas = document.createElement('canvas');
  // Bumped whenever the camera stops or restarts, which ends the running start / frame loops.
  private cameraRun = 0;
  private nextFrameAt = 0;
  private lastAnswerAt: number | null = null;
  private frameIntervalMs: number | null = null;
  private lastFrameIndex = 0;
  private zoomVersion = 0;
  private lastZoomAt = 0;
  // The engine holds this event's current registrants, so a face that matches none isn't theirs.
  private galleryReady = false;
  // Faces announced as not registered, with the engine frame each was last in view. A face is
  // announced once while its box stays up; once the box has gone, it can be announced again.
  private announcedTracks = new Map<number, number>();
  // Queue entries that aren't engine check-ins count down, so they never clash with its sequence.
  private localSequence = 0;
  private galleryLoading = false;
  private galleryReloadQueued = false;
  private hubWasDown = false;
  private destroyed = false;
  private bannerVersion = 0;
  private bannerNoticeTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.realtime.faceGalleryChanged$
      .pipe(
        filter((n) => n.eventId === this.eventId),
        debounceTime(GALLERY_CHANGE_DEBOUNCE_MS),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.loadGallery());
    this.realtime.verifyOnlyChanged$
      .pipe(
        filter((n) => n.eventId === this.eventId),
        takeUntilDestroyed(),
      )
      .subscribe((n) => this.verifyOnly.set(n.enabled));
    // Pushes sent while the hub was down are lost — re-sync once it's back.
    effect(() => {
      if (!this.realtime.connected()) this.hubWasDown = true;
      else if (this.hubWasDown) {
        this.hubWasDown = false;
        untracked(() => this.loadGallery());
      }
    });
  }

  ngOnInit(): void {
    void this.startCamera();
    this.loadEvent();
    this.loadGallery();
    this.loadAttendees();
    this.loadCustomBanner();
    this.poll();
    this.timers.push(
      setInterval(() => this.poll(), POLL_MS),
      setInterval(() => this.loadGallery(), GALLERY_SYNC_MS),
      // Picks up banner / welcome message edits made while the TV is open.
      setInterval(() => this.loadEvent(), GALLERY_SYNC_MS),
      setInterval(() => this.loadAttendees(), ATTENDEES_REFRESH_MS),
    );
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.timers.forEach(clearInterval);
    if (this.showTimer) clearTimeout(this.showTimer);
    if (this.cameraRetry) clearTimeout(this.cameraRetry);
    if (this.bannerNoticeTimer) clearTimeout(this.bannerNoticeTimer);
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    this.stopLocalCamera();
    this.engine.stopStream().subscribe({ error: () => {} });
  }

  // The webcam is released right away, so the scanner can open it as soon as we get there.
  async exit(): Promise<void> {
    if (this.exiting()) return;
    this.exiting.set(true);
    this.stopLocalCamera();
    try {
      await firstValueFrom(this.engine.stopStream().pipe(timeout(EXIT_RELEASE_TIMEOUT_MS)));
    } catch {
      /* engine offline or slow — it starts again when the TV next opens */
    }
    void this.router.navigate(['/scanner', this.eventId]);
  }

  @HostListener('window:pagehide')
  onPageHide(): void {
    this.engine.stopStreamOnUnload();
  }

  // The camera runs only while the TV is showing. The scanner page may still be releasing it for
  // a moment, and the engine may be restarting; both are retried by themselves. The webcam opens
  // first, so the preview still shows while the engine is unavailable.
  private async startCamera(): Promise<void> {
    const run = ++this.cameraRun;
    if (!this.cameraStream() && !(await this.openWebcam(run))) return;
    try {
      await firstValueFrom(this.engine.startStream().pipe(timeout(ENGINE_TIMEOUT_MS)));
      if (run !== this.cameraRun) return;
      this.applyScanMode(); // the engine forgets both on restart
      this.applyDetectionMode();
    } catch {
      if (run === this.cameraRun) this.onCameraError();
      return;
    }
    this.lastAnswerAt = null;
    this.frameIntervalMs = null;
    this.lastFrameIndex = 0;
    this.announcedTracks.clear(); // a restarted engine numbers frames and tracks from the start
    for (let i = 0; i < FRAMES_IN_FLIGHT; i++) void this.sendFrames(run);
  }

  // False when the webcam didn't open (a retry is scheduled unless it can't help) or isn't
  // needed any more.
  private async openWebcam(run: number): Promise<boolean> {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } },
        audio: false,
      });
      if (run !== this.cameraRun) {
        stream.getTracks().forEach((t) => t.stop());
        return false;
      }
      this.cameraProblem.set(null);
      this.cameraStream.set(stream);
      return true;
    } catch (err) {
      if (run !== this.cameraRun) return false;
      const failure: CameraFailure = cameraFailure(err);
      this.cameraProblem.set(
        failure === 'other' ? 'Could not open the camera. Check that it is connected.' : CAMERA_FAILURE_MESSAGES[failure],
      );
      if (failure !== 'insecure') this.onCameraError();
      return false;
    }
  }

  private stopLocalCamera(): void {
    this.cameraRun++;
    this.cameraStream()?.getTracks().forEach((t) => t.stop());
    this.cameraStream.set(null);
    this.faceBoxes.set([]);
    this.faceZooms.set([]);
    this.announcedTracks.clear();
    this.frameRoundTripMs.set(null);
    this.framesPerSecond.set(0);
  }

  // One of FRAMES_IN_FLIGHT senders: sends the camera's newest picture, waits for the answer, then
  // repeats. On a slow network or engine fewer frames go through, but they are always current and
  // never pile up (the engine only ever works on the newest one it has).
  private async sendFrames(run: number): Promise<void> {
    while (run === this.cameraRun) {
      await this.waitForFrameTurn();
      if (run !== this.cameraRun) return;
      const startedAt = performance.now();
      try {
        const frame = await this.grabFrame();
        if (frame && run === this.cameraRun) {
          const snapshot = await firstValueFrom(this.engine.sendFrame(frame).pipe(timeout(FRAME_TIMEOUT_MS)));
          if (run !== this.cameraRun) return;
          this.onFrameAnswered(snapshot, performance.now() - startedAt);
        }
      } catch {
        // The engine restarted or the network dropped: start it again once it's reachable.
        if (run === this.cameraRun) {
          this.faceBoxes.set([]);
          this.faceZooms.set([]);
          this.onCameraError();
        }
        return;
      }
    }
  }

  // Spaces the senders' frames at least FRAME_MIN_INTERVAL_MS apart.
  private async waitForFrameTurn(): Promise<void> {
    const now = performance.now();
    const turn = Math.max(now, this.nextFrameAt);
    this.nextFrameAt = turn + FRAME_MIN_INTERVAL_MS;
    if (turn > now) await new Promise((resolve) => setTimeout(resolve, turn - now));
  }

  // Null until the video has a picture.
  private grabFrame(): Promise<Blob | null> {
    const video = this.cameraVideo?.nativeElement;
    if (!video || !video.videoWidth) return Promise.resolve(null);
    const canvas = this.frameCanvas;
    canvas.width = Math.min(FRAME_WIDTH, video.videoWidth);
    canvas.height = Math.round((video.videoHeight * canvas.width) / video.videoWidth);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(video, 0, 0, canvas.width, canvas.height);
    return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', FRAME_JPEG_QUALITY));
  }

  private onFrameAnswered(snapshot: EngineSnapshot, roundTripMs: number): void {
    // With two frames on their way, an older answer can arrive after a newer one.
    if (snapshot.frame_index < this.lastFrameIndex) return;
    const processed = snapshot.frame_index - this.lastFrameIndex;
    this.lastFrameIndex = snapshot.frame_index;
    this.announceNotRegistered(snapshot);
    // Once a face has been announced as not registered, it stops being marked on the preview.
    const marked = snapshot.tracks.filter(
      (track) => !(track.status === 'unknown' && this.announcedTracks.has(track.track_id)),
    );
    this.faceBoxes.set(marked.map((track) => faceBox(track, snapshot)));
    this.frameRoundTripMs.update((previous) => smoothed(previous, roundTripMs));
    // Two answers can report the same processed frame when the engine skipped an older one.
    const now = performance.now();
    if (now - this.lastZoomAt >= ZOOM_REFRESH_MS) {
      this.lastZoomAt = now;
      this.zoomVersion++;
    }
    this.faceZooms.set(
      marked
        .filter((track) => track.head_pose.is_angled)
        .slice(0, MAX_ZOOMS)
        .map((track) => ({ id: track.track_id, url: this.engine.zoomUrl(track, this.zoomVersion) })),
    );
    if (processed > 0) {
      if (this.lastAnswerAt !== null) {
        // Smooth the time per frame, not the rate: answers arrive in bursts, which inflate a rate.
        this.frameIntervalMs = smoothed(this.frameIntervalMs, (now - this.lastAnswerAt) / processed);
        this.framesPerSecond.set(1000 / this.frameIntervalMs);
      }
      this.lastAnswerAt = now;
    }
  }

  // A registrant with no photo isn't in the gallery either, so they show as not registered too.
  private announceNotRegistered(snapshot: EngineSnapshot): void {
    // Forget faces that have gone first, so one coming back in this frame counts as new.
    for (const [trackId, lastSeenFrame] of this.announcedTracks) {
      if (snapshot.frame_index - lastSeenFrame >= NOT_REGISTERED_GONE_FRAMES) this.announcedTracks.delete(trackId);
    }
    for (const track of snapshot.tracks) {
      if (this.announcedTracks.has(track.track_id)) this.announcedTracks.set(track.track_id, snapshot.frame_index);
    }
    if (!this.galleryReady) return;
    const fresh = snapshot.tracks.filter(
      (track) => !this.announcedTracks.has(track.track_id) && isNotRegistered(track, snapshot.frame_width),
    );
    if (!fresh.length) return;
    for (const track of fresh) {
      this.announcedTracks.set(track.track_id, snapshot.frame_index);
      this.waiting.update((list) => [
        ...list,
        {
          sequence: --this.localSequence,
          name: 'Not Registered For This Event',
          photo: this.faceCrop(track),
          result: { ...this.errorResult('Not Registered For This Event', ''), status: 'notregistered' },
        },
      ]);
    }
    this.showNext();
  }

  // The face as the last frame sent shows it; this picture stays in the browser.
  private faceCrop(track: EngineTrack): string {
    const { x1, y1, x2, y2 } = track.bbox;
    const side = Math.max(x2 - x1, y2 - y1) * (1 + 2 * NOT_REGISTERED_PHOTO_MARGIN);
    const crop = document.createElement('canvas');
    crop.width = crop.height = NOT_REGISTERED_PHOTO_SIZE;
    crop
      .getContext('2d')!
      .drawImage(
        this.frameCanvas,
        (x1 + x2 - side) / 2,
        (y1 + y2 - side) / 2,
        side,
        side,
        0,
        0,
        NOT_REGISTERED_PHOTO_SIZE,
        NOT_REGISTERED_PHOTO_SIZE,
      );
    return crop.toDataURL('image/jpeg', FRAME_JPEG_QUALITY);
  }

  onCameraMetadata(video: HTMLVideoElement): void {
    if (video.videoWidth && video.videoHeight) this.cameraAspect.set(video.videoWidth / video.videoHeight);
  }

  setScanMode(mode: EngineScanMode): void {
    if (mode === this.scanMode()) return;
    this.scanMode.set(mode);
    try {
      localStorage.setItem(SCAN_MODE_KEY, mode);
    } catch {
      /* storage unavailable — keep the in-memory choice */
    }
    this.applyScanMode();
  }

  private applyScanMode(): void {
    this.engine.setScanMode(this.scanMode()).subscribe({ error: () => {} });
  }

  setDetectionMode(mode: EngineDetectionMode): void {
    if (mode === this.detectionMode()) return;
    this.detectionMode.set(mode);
    try {
      localStorage.setItem(DETECTION_MODE_KEY, mode);
    } catch {
      /* storage unavailable — keep the in-memory choice */
    }
    this.applyDetectionMode();
  }

  private applyDetectionMode(): void {
    this.engine.setDetectionMode(this.detectionMode()).subscribe({ error: () => {} });
  }

  loadGallery(): void {
    // One reload at a time; a change that comes in mid-reload gets one more pass afterwards.
    if (this.galleryLoading) {
      this.galleryReloadQueued = true;
      return;
    }
    this.galleryLoading = true;
    this.galleryReady = false;
    this.engine.loadGallery(this.eventId).subscribe({
      complete: () => this.onGalleryLoaded(),
      next: () => {
        this.galleryError.set(false);
        this.galleryReady = true;
      },
      error: (err) => {
        this.galleryError.set(true);
        this.galleryMessage.set(err?.error?.detail ?? 'Could not load registered faces from the face engine.');
        this.onGalleryLoaded();
      },
    });
  }

  private onGalleryLoaded(): void {
    this.galleryLoading = false;
    if (this.galleryReloadQueued) {
      this.galleryReloadQueued = false;
      this.loadGallery();
    }
  }

  private loadEvent(): void {
    this.api.getEvent(this.eventId).subscribe({
      next: (e) => {
        this.eventTitle.set(e.title);
        this.eventWelcome.set(e.welcomeMessage);
        this.verifyOnly.set(e.verifyOnly);
        this.eventBanner.set(e.bannerImage);
      },
      error: () => {},
    });
  }

  loadAttendees(): void {
    this.api.attendance(this.eventId).subscribe({ next: (list) => this.attendees.set(list), error: () => {} });
  }

  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if ((event.key === 'f' || event.key === 'F') && !event.ctrlKey && !event.metaKey) this.toggleFullscreen();
  }

  toggleFullscreen(): void {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    else {
      // Going fullscreen gives the stage the whole screen; "Attendees" brings the list back.
      this.sideOpen.set(false);
      document.documentElement.requestFullscreen().catch(() => {});
    }
  }

  // The preview sits in the bottom-left corner, so dragging the handle up or right grows it.
  startCameraResize(event: PointerEvent): void {
    event.preventDefault();
    const handle = event.currentTarget as HTMLElement;
    const preview = handle.parentElement!;
    const startWidth = preview.getBoundingClientRect().width;
    const { clientX: startX, clientY: startY } = event;
    handle.setPointerCapture(event.pointerId);

    const move = (e: PointerEvent) => {
      const dx = e.clientX - startX;
      const dy = (startY - e.clientY) * CAMERA_ASPECT;
      this.cameraWidth.set(clampCameraWidth(startWidth + (Math.abs(dx) > Math.abs(dy) ? dx : dy)));
    };
    const end = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', end);
      handle.removeEventListener('pointercancel', end);
      this.saveCameraWidth();
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
  }

  onCameraResizeKey(event: KeyboardEvent): void {
    const steps: Record<string, number> = { ArrowUp: 1, ArrowRight: 1, ArrowDown: -1, ArrowLeft: -1 };
    const step = steps[event.key];
    if (!step) return;
    event.preventDefault();
    const preview = (event.currentTarget as HTMLElement).parentElement!;
    this.cameraWidth.set(clampCameraWidth(preview.getBoundingClientRect().width + step * CAMERA_KEY_STEP));
    this.saveCameraWidth();
  }

  resetCameraSize(): void {
    this.cameraWidth.set(null);
    this.saveCameraWidth();
  }

  private saveCameraWidth(): void {
    const width = this.cameraWidth();
    try {
      if (width === null) localStorage.removeItem(CAMERA_WIDTH_KEY);
      else localStorage.setItem(CAMERA_WIDTH_KEY, String(width));
    } catch {
      /* storage unavailable — keep the in-memory size */
    }
  }

  private onCameraError(): void {
    if (this.cameraRetry || !this.cameraOn() || this.destroyed) return;
    // Start again too (a no-op if it's running): the engine may have restarted, or the camera
    // was released by another app.
    this.cameraRetry = setTimeout(() => {
      this.cameraRetry = null;
      if (this.cameraOn()) void this.startCamera();
    }, CAMERA_RETRY_MS);
  }

  // ---- Camera on / off ----

  async setCamera(on: boolean): Promise<void> {
    if (this.cameraSwitching() || on === this.cameraOn()) return;
    this.cameraSwitching.set(true);
    try {
      if (on) {
        this.cameraOn.set(true);
        void this.startCamera();
      } else {
        this.cameraOn.set(false);
        this.clearShown();
        if (this.cameraRetry) {
          clearTimeout(this.cameraRetry);
          this.cameraRetry = null;
        }
        this.stopLocalCamera();
        this.cameraProblem.set(null);
        await firstValueFrom(this.engine.stopStream().pipe(timeout(EXIT_RELEASE_TIMEOUT_MS)));
      }
    } catch {
      /* engine offline or slow — it has no stream to show either way */
    } finally {
      this.cameraSwitching.set(false);
    }
  }

  // Drops the current and queued welcome screens.
  private clearShown(): void {
    if (this.showTimer) clearTimeout(this.showTimer);
    this.showTimer = null;
    this.current.set(null);
    this.waiting.set([]);
  }

  // ---- Banner display ----

  setBannerFit(fit: BannerFit): void {
    this.bannerFit.set(fit);
    try {
      localStorage.setItem(BANNER_FIT_KEY, fit);
    } catch {
      /* storage unavailable — keep the in-memory choice */
    }
  }

  private async loadCustomBanner(): Promise<void> {
    const version = this.bannerVersion;
    try {
      const saved = await this.tvBanners.load(this.eventId);
      if (!this.destroyed && version === this.bannerVersion) this.customBanner.set(saved);
    } catch {
      /* no saved banner, or storage unavailable — the event banner is used */
    }
  }

  // PNG / JPEG / WebP up to 10 MB; only while the camera is off.
  async uploadBanner(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || !this.canEditBanner()) return;
    const version = ++this.bannerVersion;
    this.bannerBusy.set(true);
    try {
      const banner = await this.tvBanners.prepare(file, this.eventId);
      if (this.destroyed || version !== this.bannerVersion || this.cameraOn()) return;
      this.customBanner.set(banner);
      try {
        await this.tvBanners.save(banner);
      } catch {
        this.showBannerNotice('Banner shown for this session only — browser storage is unavailable.');
      }
    } catch (error) {
      if (version === this.bannerVersion)
        this.showBannerNotice(error instanceof Error ? error.message : 'Could not read the banner image.');
    } finally {
      this.bannerBusy.set(false);
    }
  }

  async removeBanner(): Promise<void> {
    if (!this.canEditBanner() || !this.customBanner()) return;
    ++this.bannerVersion;
    this.bannerBusy.set(true);
    try {
      await this.tvBanners.remove(this.eventId);
      this.customBanner.set(null);
    } catch {
      this.showBannerNotice('Could not remove the saved banner. Please try again.');
    } finally {
      this.bannerBusy.set(false);
    }
  }

  private showBannerNotice(message: string): void {
    this.bannerNotice.set(message);
    if (this.bannerNoticeTimer) clearTimeout(this.bannerNoticeTimer);
    this.bannerNoticeTimer = setTimeout(() => {
      this.bannerNoticeTimer = null;
      this.bannerNotice.set(null);
    }, BANNER_NOTICE_MS);
  }

  private async poll(): Promise<void> {
    if (this.isPolling) return;
    this.isPolling = true;
    try {
      const data = await firstValueFrom(this.engine.checkIns(this.lastSequence).pipe(timeout(ENGINE_TIMEOUT_MS)));
      this.engineOnline.set(true);
      const isFirstLoad = this.sessionId === null;
      if (data.session_id !== this.sessionId) {
        // The engine started a new session: sequence numbers restarted.
        this.sessionId = data.session_id;
        this.lastSequence = 0;
        this.waiting.set([]);
        if (!isFirstLoad) {
          // A restarted engine has no gallery until it's told which event to load.
          this.loadGallery();
          this.isPolling = false;
          await this.poll();
          return;
        }
      }
      this.displaySeconds.set(data.display_seconds);
      this.lastSequence = data.last_sequence;
      const replayAfter = Date.now() / 1000 - REPLAY_RECENT_SECONDS;
      const fresh = this.cameraOn()
        ? data.check_ins.filter((c) => !isFirstLoad || c.checked_in_at >= replayAfter)
        : [];
      // People join the queue right away, in the order they were recognized; the backend's answers
      // fill in as they arrive, without holding up the next poll.
      if (fresh.length) {
        this.waiting.update((list) => [...list, ...fresh.map((c) => this.toItem(c))]);
        void Promise.all(fresh.map((c) => this.record(c))).then(() => this.loadAttendees());
      }
      this.showNext();
    } catch {
      this.engineOnline.set(false); // engine restarting: keep the screen and retry
    } finally {
      this.isPolling = false;
    }
  }

  // What the engine knows, straight from the engine. A QR pass has no engine photo; only the
  // backend knows whose pass it is.
  private toItem(checkIn: EngineCheckIn): TvItem {
    const { person } = checkIn;
    const isQr = person.person_id.startsWith('q');
    return {
      sequence: checkIn.sequence,
      name: person.name || (isQr ? 'QR pass' : 'Guest'),
      photo: isQr ? '' : this.engine.photoUrl(person),
      result: null,
    };
  }

  /** Record the check-in in the backend; its answer decides what the TV shows. */
  private async record(checkIn: EngineCheckIn): Promise<void> {
    const { person } = checkIn;
    let result: CheckInResult;
    try {
      result = await firstValueFrom(this.checkInRequest(person.person_id).pipe(timeout(CHECK_IN_TIMEOUT_MS)));
    } catch {
      result = this.errorResult(person.name, 'Could not record this check-in. Please see the registration desk.');
    }
    const answered = (item: TvItem): TvItem =>
      item.sequence === checkIn.sequence
        ? { ...item, result, name: result.fullName || item.name, photo: item.photo || result.photoUrl || '' }
        : item;
    this.waiting.update((list) => list.map(answered));
    const current = this.current();
    if (current?.sequence === checkIn.sequence) {
      this.current.set(answered(current));
      this.startDisplayTimer();
    }
  }

  private checkInRequest(personId: string) {
    if (personId.startsWith('u')) return this.api.checkInUser(this.eventId, Number(personId.slice(1)));
    // "g{token}": a guest registration's face; "q{token}": a QR pass the camera read.
    return this.api.checkIn(personId.slice(1));
  }

  private showNext(): void {
    if (this.current()) return;
    const [next, ...rest] = this.waiting();
    if (!next) return;
    this.waiting.set(rest);
    this.current.set(next);
    if (next.result) this.startDisplayTimer();
  }

  // Runs from the backend's answer, so everyone's status gets its full time on screen.
  private startDisplayTimer(): void {
    this.showTimer = setTimeout(() => {
      this.showTimer = null;
      this.current.set(null);
      this.showNext();
    }, this.displaySeconds() * 1000);
  }

  private errorResult(name: string, message: string): CheckInResult {
    return {
      status: 'error',
      fullName: name,
      eventTitle: null,
      checkedInAt: null,
      message,
      level: null,
      senateNo: null,
      organization: null,
      welcomeMessage: null,
      photoUrl: null,
      sessionTitle: null,
    };
  }

  private formatTime(date: Date): string {
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }
}
