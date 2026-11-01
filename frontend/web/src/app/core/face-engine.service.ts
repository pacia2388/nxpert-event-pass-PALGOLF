import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { FACE_ENGINE_BASE } from './config';

// A person the engine recognized. person_id is the backend's id: "u{userId}" for an account
// registration, "g{token}" for a guest registration.
export interface EnginePerson {
  person_id: string;
  name: string;
  photo_url: string;
}

export interface EngineCheckIn {
  sequence: number;
  person: EnginePerson;
  similarity: number;
  checked_in_at: number; // unix seconds
}

export interface EngineCheckInList {
  session_id: string;
  last_sequence: number;
  display_seconds: number;
  check_ins: EngineCheckIn[];
}

/** What the engine's camera checks people in with: faces, QR passes, or both. */
export type EngineScanMode = 'face' | 'qr' | 'auto';

/**
 * When the engine identifies a face: 'steady' once the person stands or moves a little (not
 * walking past), 'continuous' right away, even someone walking quickly.
 */
export type EngineDetectionMode = 'steady' | 'continuous';

/** A face the engine is following, with its box in the pixels of the frame it was sent. */
export interface EngineTrack {
  track_id: number;
  status: 'identifying' | 'matched' | 'unknown';
  bbox: { x1: number; y1: number; x2: number; y2: number };
  similarity: number | null;
  person: EnginePerson | null;
  is_steady: boolean;
  recognition_count: number;
  seconds_tracked: number;
  head_pose: { is_angled: boolean };
  // The face enlarged, with its eyes-nose-mouth structure drawn on by the engine.
  zoom_url: string;
}

export interface EngineSnapshot {
  frame_index: number;
  frame_width: number;
  frame_height: number;
  detection_mode: EngineDetectionMode;
  tracks: EngineTrack[];
}

export interface EngineGallerySummary {
  event_id: number;
  person_count: number;
  template_count: number;
  skipped_people: string[];
}

/**
 * Talks to the face recognition engine. It uses HttpBackend, so requests skip the app's
 * interceptors: the user's JWT is never sent to the engine, and the TV's fast poll doesn't
 * raise error toasts while the engine restarts.
 */
@Injectable({ providedIn: 'root' })
export class FaceEngineService {
  private http = new HttpClient(inject(HttpBackend));

  private url(path: string) {
    return `${FACE_ENGINE_BASE}/api/v1${path}`;
  }

  /** Load the event's registrants (their backend photos) as the engine's gallery. */
  loadGallery(eventId: number): Observable<EngineGallerySummary> {
    return this.http.post<EngineGallerySummary>(this.url(`/faces/reload?event_id=${eventId}`), null);
  }

  /** People recognized after `afterSequence`. Polling this is what keeps camera check-ins on. */
  checkIns(afterSequence: number): Observable<EngineCheckInList> {
    return this.http.get<EngineCheckInList>(this.url(`/check-ins?after_sequence=${afterSequence}`));
  }

  /** Clear the engine's queue so everyone in view can be recognized again right away. */
  resetCheckIns(): Observable<void> {
    return this.http.delete<void>(this.url('/check-ins'));
  }

  /** Get the engine ready for this browser's webcam frames; the server has no camera of its own. */
  startStream(): Observable<unknown> {
    return this.http.post(this.url('/stream/start'), { source: 'browser' });
  }

  /** Send one webcam frame. The answer comes once the engine has processed it. */
  sendFrame(frame: Blob): Observable<EngineSnapshot> {
    const body = new FormData();
    body.append('image', frame, 'frame.jpg');
    return this.http.post<EngineSnapshot>(this.url('/stream/frame'), body);
  }

  /** Tell the engine no more frames are coming. */
  stopStream(): Observable<unknown> {
    return this.http.post(this.url('/stream/stop'), null);
  }

  /** Choose what the camera looks for. A QR pass arrives as a check-in for person "q{token}". */
  setScanMode(mode: EngineScanMode): Observable<unknown> {
    return this.http.post(this.url('/stream/mode'), { mode });
  }

  /** Choose when faces are identified: once the person is steady, or continuously. */
  setDetectionMode(mode: EngineDetectionMode): Observable<unknown> {
    return this.http.post(this.url('/stream/detection'), { mode });
  }

  /** Stop the engine while the page is unloading (refresh / tab close), when HttpClient can't. */
  stopStreamOnUnload(): void {
    navigator.sendBeacon?.(this.url('/stream/stop'));
  }

  /** `version` makes the browser fetch a fresh picture; the engine redraws it every frame. */
  zoomUrl(track: EngineTrack, version: number): string {
    return `${FACE_ENGINE_BASE}${track.zoom_url}?t=${version}`;
  }

  photoUrl(person: EnginePerson): string {
    return `${FACE_ENGINE_BASE}${person.photo_url}`;
  }
}
