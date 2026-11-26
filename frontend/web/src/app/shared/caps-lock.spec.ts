import { capsLockOn } from './caps-lock';

describe('capsLockOn', () => {
  beforeEach(() => capsLockOn.set(false));

  it('reads the Caps Lock state from any key press on the page, before a field has focus', () => {
    document.body.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', modifierCapsLock: true }),
    );
    expect(capsLockOn()).toBe(true);

    document.body.dispatchEvent(
      new KeyboardEvent('keyup', { key: 'Tab', modifierCapsLock: false }),
    );
    expect(capsLockOn()).toBe(false);
  });
});
