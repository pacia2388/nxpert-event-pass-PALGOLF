import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { MailAttachmentsComponent } from './mail-attachments';

describe('mail attachments', () => {
  function component(): MailAttachmentsComponent {
    TestBed.resetTestingModule();
    return TestBed.createComponent(MailAttachmentsComponent).componentInstance;
  }

  it('totals the kept existing files and the new ones against 25 MB', () => {
    const c = component();
    c.files.set([new File([new Uint8Array(1024)], 'a.pdf')]);
    c.removed.set(['old.pdf']);

    expect(c.totalBytes()).toBe(1024);
    expect(c.percent()).toBe(0);
    expect(c.overLimit()).toBe(false);
  });

  it('keeps existing files unless removed and exposes the kept names', () => {
    const c = component();

    expect(c.keptNames()).toEqual([]);
    c.files.set([new File(['x'], 'notes.txt')]);
    expect(c.files().map((f) => f.name)).toEqual(['notes.txt']);

    c.removeNew(0);
    expect(c.files()).toEqual([]);
  });

  it('reports when the total goes over the limit', () => {
    const c = component();
    c.files.set([new File([new Uint8Array(26 * 1024 * 1024)], 'huge.bin')]);

    expect(c.overLimit()).toBe(true);
    expect(c.percent()).toBe(100);
  });
});
