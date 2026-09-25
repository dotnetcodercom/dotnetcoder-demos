import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { App, RiskyPanel } from './app';

@Component({
  selector: 'app-projecting-wrapper',
  template: `
    @boundary {
      <ng-content />
    } @error {
      <p data-testid="wrapper-fallback">Wrapper fallback</p>
    }
  `,
})
class ProjectingWrapper {}

@Component({
  imports: [ProjectingWrapper, RiskyPanel],
  template: `
    <app-projecting-wrapper>
      <app-risky-panel [failureMode]="'known'" />
    </app-projecting-wrapper>
  `,
})
class ProjectedHost {}

describe('Angular 22.2 error boundaries', () => {
  it('replaces a failing view with its fallback while preserving the page', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('[data-testid="known-error"]')?.textContent).toContain('could not render');
    expect(host.querySelector('[data-testid="outside"]')?.textContent).toContain('stays visible');
    expect(host.querySelector('[data-testid="panel"]')).toBeNull();
  });

  it('re-renders the view after the cause is fixed and $reset is called', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('[data-testid="resolve"]') as HTMLButtonElement).click();
    (host.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="panel"]')?.textContent).toContain('Report ready');
    expect(host.querySelector('[data-testid="known-error"]')).toBeNull();
  });

  it('routes an unclassified rendering error to the catch-all fallback', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('[data-testid="other"]') as HTMLButtonElement).click();
    (host.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="unknown-error"]')?.textContent).toContain('Unexpected');
    expect(host.querySelector('[data-testid="known-error"]')).toBeNull();
  });

  it('does not catch projected content through a wrapper ng-content boundary', () => {
    const fixture = TestBed.createComponent(ProjectedHost);
    expect(() => fixture.detectChanges()).toThrowError('The report could not render');
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('[data-testid="wrapper-fallback"]')).toBeNull();
  });
});
