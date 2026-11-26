import { signal } from '@angular/core';

/**
 * Caps Lock state for the whole page. A browser only reveals it on a key event, so listen for every
 * key press from the start: by the time the user reaches a password field (the Tab press that got
 * there counts) the state is already known, even when Caps Lock was on before the page loaded.
 */
export const capsLockOn = signal(false);

const update = (event: KeyboardEvent) =>
  capsLockOn.set(event.getModifierState?.('CapsLock') ?? false);

// Capture phase: the state is read before the key reaches any field.
document.addEventListener('keydown', update, true);
document.addEventListener('keyup', update, true);
