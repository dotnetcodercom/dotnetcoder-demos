import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withAutoCleanupInjectors } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { App, RouteScopedService, routes } from './app';

describe('Angular 22.2 route injector cleanup', () => {
  beforeEach(() => {
    RouteScopedService.created = 0;
    RouteScopedService.destroyed = 0;
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes, withAutoCleanupInjectors())],
    });
  });

  it('destroys a route provider after navigating away', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/reports/1');
    fixture.detectChanges();
    expect(RouteScopedService.created).toBe(1);
    expect(RouteScopedService.destroyed).toBe(0);

    await router.navigateByUrl('/');
    fixture.detectChanges();
    expect(RouteScopedService.destroyed).toBe(1);
  });

  it('keeps the route injector while only a parameter changes', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/reports/1');
    fixture.detectChanges();
    await router.navigateByUrl('/reports/2');
    fixture.detectChanges();

    expect(RouteScopedService.created).toBe(1);
    expect(RouteScopedService.destroyed).toBe(0);
  });
});

describe('without opt-in cleanup', () => {
  beforeEach(() => {
    RouteScopedService.created = 0;
    RouteScopedService.destroyed = 0;
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    });
  });

  it('retains the route provider when navigating away', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/reports/1');
    fixture.detectChanges();
    await router.navigateByUrl('/');
    fixture.detectChanges();
    expect(RouteScopedService.created).toBe(1);
    expect(RouteScopedService.destroyed).toBe(0);
  });
});
