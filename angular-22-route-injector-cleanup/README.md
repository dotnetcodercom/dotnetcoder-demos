# Angular 22.2 route injector cleanup proof

This small Angular app tests the lifetime of a service provided by a route. It uses Angular 22.2.0 and `withAutoCleanupInjectors()`.

Companion article: https://dotnetcoder.com/angular-22-route-injector-cleanup/

## Prerequisites

Requires Node.js 20.19+ and npm.

## Run

```bash
npm ci
npm run check
npm start
```

Open the address printed by `ng serve`. Navigate Home → Reports 1 → Reports 2 → Home. The service is created once when entering Reports, remains alive across a parameter-only navigation, and is destroyed when leaving the route. A separate automated test shows that the service stays alive on leaving when the cleanup feature is omitted.

`npm run check` builds the app and runs three tests in jsdom. This is a behavior proof, not a memory usage benchmark. A custom `RouteReuseStrategy` that does not extend `BaseRouteReuseStrategy` needs `shouldDestroyInjector`; stored detached handles also require `retrieveStoredRouteHandles`. See [Angular's route behavior guide](https://angular.dev/guide/routing/customizing-route-behavior) and [API reference](https://angular.dev/api/router/withAutoCleanupInjectors).
