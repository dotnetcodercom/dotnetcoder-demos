import { Component, input, signal } from '@angular/core';

export class DataUnavailableError extends Error {}
export type FailureMode = 'known' | 'unknown' | 'none';

@Component({
  selector: 'app-risky-panel',
  template: `<p data-testid="panel">{{ renderValue() }}</p>`,
})
export class RiskyPanel {
  readonly failureMode = input.required<FailureMode>();

  renderValue(): string {
    if (this.failureMode() === 'known') throw new DataUnavailableError('The report could not render');
    if (this.failureMode() === 'unknown') throw new Error('Unexpected component failure');
    return 'Report ready';
  }
}

@Component({
  selector: 'app-root',
  imports: [RiskyPanel],
  template: `
    <h1>Angular 22.2 Error Boundary</h1>
    <p data-testid="outside">The rest of the page stays visible.</p>
    <button type="button" data-testid="resolve" (click)="failureMode.set('none')">Resolve cause</button>
    <button type="button" data-testid="other" (click)="failureMode.set('unknown')">Trigger other error</button>
    @boundary {
      <app-risky-panel [failureMode]="failureMode()" />
    } @error (let err; reset = $reset; when isDataUnavailable(err)) {
      <p role="alert" data-testid="known-error">{{ err.message }}</p>
      <button type="button" data-testid="retry" (click)="reset()">Retry report</button>
    } @error {
      <p role="alert" data-testid="unknown-error">Unexpected rendering error</p>
    }
  `,
})
export class App {
  readonly failureMode = signal<FailureMode>('known');

  isDataUnavailable(error: unknown): boolean {
    return error instanceof DataUnavailableError;
  }
}
