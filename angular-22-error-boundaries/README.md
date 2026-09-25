# Angular 22.2 Error Boundaries demo

This small Angular 22.2.0 application demonstrates a local rendering fallback, an explicit retry after the cause is fixed, and a catch-all fallback for a different error. The accompanying article is planned for https://dotnetcoder.com/angular-22-error-boundaries/ (URL subject to editorial review).

`@boundary` is in developer preview. Keep the Angular version pinned when reproducing the behavior.

## Prerequisites

- Node.js 20.19+ or another version supported by Angular 22.2.0
- npm

## Run

```bash
npm ci
npm run check
npm start
```

Open the local address printed by `ng serve`. The page starts with a known render failure. Click **Resolve cause** and then **Retry report** to recover. Reload, then click **Trigger other error** before retrying to inspect the catch-all fallback.

The four tests cover known error fallback with the rest of the page intact, recovery after `$reset()`, unclassified error fallback, and the projected-content limitation. `npm run check` compiles a production build and runs the tests in jsdom. Expected render errors may be printed by Angular's default `ErrorHandler` during the passing tests; those messages do not mean the tests failed.

The example deliberately does not claim that a boundary catches every error. In particular, Angular's documentation says that a boundary around `<ng-content>` does not catch failures from projected content. See the [Angular error boundaries guide](https://angular.dev/guide/templates/error-boundaries).
