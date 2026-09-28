import { Component, Injectable, OnDestroy, inject } from '@angular/core';
import { RouterLink, RouterOutlet, Routes } from '@angular/router';

@Injectable()
export class RouteScopedService implements OnDestroy {
  static created = 0;
  static destroyed = 0;

  constructor() {
    RouteScopedService.created++;
  }

  ngOnDestroy(): void {
    RouteScopedService.destroyed++;
  }
}

@Component({ template: `<p data-testid="reports">Reports route active</p>` })
export class ReportsPage {
  private readonly routeService = inject(RouteScopedService);
}

@Component({ template: `<p data-testid="home">Home route active</p>` })
export class HomePage {}

export const routes: Routes = [
  { path: '', component: HomePage },
  { path: 'reports/:id', component: ReportsPage, providers: [RouteScopedService] },
];

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  template: `
    <h1>Angular 22.2 route injector cleanup</h1>
    <nav aria-label="Example routes">
      <a routerLink="/">Home</a> |
      <a routerLink="/reports/1">Reports 1</a> |
      <a routerLink="/reports/2">Reports 2</a>
    </nav>
    <p>Route service created: {{ created }}; destroyed: {{ destroyed }}</p>
    <router-outlet />
  `,
})
export class App {
  get created(): number { return RouteScopedService.created; }
  get destroyed(): number { return RouteScopedService.destroyed; }
}
