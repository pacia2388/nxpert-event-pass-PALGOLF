import { afterEach, describe, expect, it } from 'vitest';
import {
  DEFAULT_IMAGE_LIMITS,
  IMAGE_LIMITS,
  applyImageLimits,
  fitScale,
  imageLimitMessage,
  imageLimitsHelp,
  pngBytes,
} from './rich-text';

describe('inline image limits', () => {
  afterEach(() => applyImageLimits(DEFAULT_IMAGE_LIMITS));

  it('reads the decoded size of a base64 PNG, allowing for padding', () => {
    // "TWFu" is 3 bytes, "TWE=" is 2 bytes ("=" padding) and "TQ==" is 1 byte.
    expect(pngBytes('data:image/png;base64,TWFu')).toBe(3);
    expect(pngBytes('data:image/png;base64,TWE=')).toBe(2);
    expect(pngBytes('data:image/png;base64,TQ==')).toBe(1);
    expect(pngBytes('')).toBe(0);
  });

  it('uses the configured limits in the help and error text', () => {
    applyImageLimits({ maxCount: 3, maxImageBytes: 1024 * 1024, maxTotalBytes: 4 * 1024 * 1024 });

    expect(IMAGE_LIMITS.maxCount).toBe(3);
    expect(imageLimitsHelp()).toContain('up to 3');
    expect(imageLimitsHelp()).toContain('1 MB each');
    expect(imageLimitsHelp()).toContain('4 MB total');
    expect(imageLimitMessage()).toContain('up to 3');
  });

  it('ignores nonsense values, keeping the previous limit', () => {
    applyImageLimits({ maxCount: 2 });
    applyImageLimits({ maxCount: 0, maxImageBytes: Number.NaN });

    expect(IMAGE_LIMITS.maxCount).toBe(2);
    expect(IMAGE_LIMITS.maxImageBytes).toBe(DEFAULT_IMAGE_LIMITS.maxImageBytes);
  });

  it('shrinks an image that does not fit, at least 10% per step', () => {
    const available = 100_000;
    const next = fitScale(1, 400_000, available);

    expect(next).toBeLessThan(0.9);
    expect(next).toBeGreaterThan(0);
    expect(next * next * 400_000).toBeLessThanOrEqual(available * 1.1);
  });

  it('never enlarges an image', () => {
    expect(fitScale(0.5, 10, 1_000_000)).toBeLessThanOrEqual(0.45);
    expect(fitScale(0.5, 10, 1_000_000)).toBeGreaterThan(0);
  });
});
