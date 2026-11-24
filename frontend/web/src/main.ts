import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { loadAppSettings, loadMenus } from './app/core/config';
import { loadTheme } from './app/core/theme';

Promise.all([loadAppSettings(), loadMenus()])
  // The look: the theme file appSettings.json names (never the API); it never blocks start-up.
  .then(loadTheme)
  .then(() => bootstrapApplication(App, appConfig))
  .catch((err) => {
    console.error(err);
    document.body.textContent = 'App failed to start: could not load appSettings.json.';
  });
