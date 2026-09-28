import "@testing-library/jest-dom/vitest";

class ResizeObserverStub {
  constructor(private readonly callback: ResizeObserverCallback) {}
  observe(target: Element) {
    this.callback(
      [{
        target,
        contentRect: {
          width: 1280,
          height: 800,
          top: 0,
          left: 0,
          right: 1280,
          bottom: 800,
          x: 0,
          y: 0,
          toJSON: () => ({}),
        },
      } as ResizeObserverEntry],
      this as unknown as ResizeObserver,
    );
  }
  unobserve() {}
  disconnect() {}
}

class DOMMatrixStub {
  a = 1;
  b = 0;
  c = 0;
  d = 1;
  e = 0;
  f = 0;
  inverse() {
    return this;
  }
  multiply() {
    return this;
  }
  translate() {
    return this;
  }
  scale() {
    return this;
  }
}

globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;
globalThis.DOMMatrix ??= DOMMatrixStub as unknown as typeof DOMMatrix;
globalThis.DOMMatrixReadOnly ??= DOMMatrixStub as unknown as typeof DOMMatrixReadOnly;

if (!globalThis.CSS) {
  Object.defineProperty(globalThis, "CSS", {
    value: { escape: (value: string) => value.replace(/["\\]/g, "\\$&") },
  });
} else if (!globalThis.CSS.escape) {
  globalThis.CSS.escape = (value: string) => value.replace(/["\\]/g, "\\$&");
}

Object.defineProperty(HTMLElement.prototype, "getBoundingClientRect", {
  configurable: true,
  value() {
    return {
      width: 1280,
      height: 800,
      top: 0,
      left: 0,
      right: 1280,
      bottom: 800,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    };
  },
});
