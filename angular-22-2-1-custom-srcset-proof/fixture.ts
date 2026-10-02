import '@angular/compiler';
import {Component, VERSION, provideZonelessChangeDetection} from '@angular/core';
import {bootstrapApplication} from '@angular/platform-browser';
import {IMAGE_LOADER, NgOptimizedImage, ImageLoaderConfig} from '@angular/common';

class SrcsetProof {
  custom = 'images/signed-a.png?token=one 1x, images/signed-b.png?token=two 2x';
}
Component({
  selector: 'app-root',
  standalone: true,
  imports: [NgOptimizedImage],
  template: `
    <h1>NgOptimizedImage custom srcset proof</h1>
    <p>Compare the same initial bindings on Angular 22.2.0 and 22.2.1.</p>
    <section><h2>[srcset], optimization disabled</h2>
      <img id="custom" ngSrc="images/fallback.png" [srcset]="custom"
           disableOptimizedSrcset width="100" height="50" loading="eager" />
    </section>
    <section><h2>[attr.srcset] workaround</h2>
      <img id="attribute" ngSrc="images/fallback.png" [attr.srcset]="custom"
           disableOptimizedSrcset width="100" height="50" loading="eager" />
    </section>
    <section><h2>Generated ngSrcset control</h2>
      <img id="generated" ngSrc="generated" ngSrcset="1x, 2x"
           width="100" height="50" loading="eager" />
    </section>
    <pre id="result">Waiting for image loading...</pre>
  `,
})(SrcsetProof);

bootstrapApplication(SrcsetProof, {
  providers: [provideZonelessChangeDetection(), {
    provide: IMAGE_LOADER,
    useValue: (config: ImageLoaderConfig) => config.src === 'generated'
      ? `images/generated-${config.width ?? 100}.png` : config.src,
  }],
}).then(() => {
  (window as any).proofReady = VERSION.full;
  const refresh = () => {
    const rows = ['custom', 'attribute', 'generated'].map(id => {
      const img = document.getElementById(id) as HTMLImageElement;
      return {id, srcset: img.getAttribute('srcset'), currentSrc: img.currentSrc,
        loaded: img.complete && img.naturalWidth > 0};
    });
    document.getElementById('result')!.textContent = JSON.stringify({
      angular: VERSION.full, devicePixelRatio: window.devicePixelRatio, rows,
    }, null, 2);
  };
  document.querySelectorAll('img').forEach(img => img.addEventListener('load', refresh));
  refresh();
}).catch(error => {
  (window as any).proofError = String(error);
  throw error;
});
