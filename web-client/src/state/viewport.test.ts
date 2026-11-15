import { describe, expect, it } from 'vitest';
import { computeViewport, overlayFor, type ViewportInput } from './viewport';
import { isIos, isStandalone } from './pwa';
import { claimable } from '../screens/Missions';

const phone = (o: Partial<ViewportInput>): ViewportInput => ({ innerW: 390, innerH: 844, screenW: 390, screenH: 844, touch: true, editableFocused: false, orientationType: 'portrait-primary', ...o });

describe('viewport rules', () => {
  it('portrait phone: lobby ok, duel needs landscape', () => {
    const v = computeViewport(phone({}));
    expect(overlayFor('portrait', v)).toBeNull();
    expect(overlayFor('landscape', v)).toBe('rotate-landscape');
  });
  it('landscape phone: duel ok, lobby asks for portrait', () => {
    const v = computeViewport(phone({ innerW: 844, innerH: 390, orientationType: 'landscape-primary' }));
    expect(overlayFor('landscape', v)).toBeNull();
    expect(overlayFor('portrait', v)).toBe('rotate-portrait');
  });
  it('landscape device but viewport not wider than tall (split view / mid-rotation) is not a fit', () => {
    const v = computeViewport(phone({ innerW: 390, innerH: 844, orientationType: 'landscape-primary' }));
    expect(v.landscapeFit).toBe(false);
    expect(overlayFor('landscape', v)).toBe('rotate-landscape');
  });
  it('iOS keyboard: only visualViewport shrinks; not mistaken for landscape, frame follows visible height', () => {
    const v = computeViewport(phone({ vvW: 390, vvH: 420, vvTop: 300, editableFocused: true }));
    expect(v.keyboard).toBe(true);
    expect(v.deviceLandscape).toBe(false);
    expect(v.h).toBe(420);
    expect(v.top).toBe(300);
    expect(overlayFor('portrait', v)).toBeNull();
  });
  it('Android keyboard shrinking the layout viewport does not flip orientation (device orientation wins)', () => {
    const v = computeViewport(phone({ innerH: 330, vvH: 330, editableFocused: true }));
    expect(v.deviceLandscape).toBe(false);
    expect(overlayFor('portrait', v)).toBeNull();
  });
  it('legacy iOS window.orientation is used when screen.orientation is missing', () => {
    const v = computeViewport(phone({ orientationType: undefined, windowOrientation: 90, innerW: 844, innerH: 390 }));
    expect(v.landscapeFit).toBe(true);
  });
  it('tablets and PCs never get a rotate overlay (they letterbox)', () => {
    const ipad = computeViewport(phone({ screenW: 820, screenH: 1180, innerW: 820, innerH: 1180 }));
    expect(overlayFor('landscape', ipad)).toBeNull();
    const pc = computeViewport({ innerW: 900, innerH: 1000, screenW: 1920, screenH: 1080, touch: false, editableFocused: false });
    expect(overlayFor('landscape', pc)).toBeNull();
    expect(overlayFor('portrait', pc)).toBeNull();
  });
});

describe('pwa + missions helpers', () => {
  it('detects iPhone and iPadOS (reports as Mac with touch)', () => {
    expect(isIos('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)', 5)).toBe(true);
    expect(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5)).toBe(true);
    expect(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0)).toBe(false);
  });
  it('standalone via display-mode or iOS navigator.standalone', () => {
    const w = (mm: boolean, st?: boolean) => ({ matchMedia: () => ({ matches: mm }), navigator: { standalone: st } }) as unknown as Window;
    expect(isStandalone(w(true))).toBe(true);
    expect(isStandalone(w(false, true))).toBe(true);
    expect(isStandalone(w(false))).toBe(false);
  });
  it('counts claimable missions', () => {
    const m = (completed: boolean, claimed: boolean) => ({ id: 'x', trigger: 't', target: 1, progress: 1, completed, claimed, rewards: {} });
    expect(claimable({ serverTime: '', daily: { periodKey: 'd', resetsAt: '', items: [m(true, false), m(true, true)] }, weekly: { periodKey: 'w', resetsAt: '', items: [m(false, false), m(true, false)] } })).toBe(2);
  });
});
