import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { PreloadAllModules, provideRouter, withComponentInputBinding, withInMemoryScrolling, withPreloading, withViewTransitions } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { routes } from './app.routes';

registerLocaleData(localePt);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'top' }), withPreloading(PreloadAllModules),
      // Troca de página: só o conteúdo faz um esmaecido cruzado curto (a barra lateral fica parada). A classe marca a transição
      // como de rota, para não misturar com as transições de filtro dentro da página.
      withViewTransitions({ skipInitialTransition: true, onViewTransitionCreated: ({ transition }) => {
        const root = document.documentElement;
        root.classList.add('route-vt');
        transition.finished.finally(() => root.classList.remove('route-vt'));
      } })),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ],
};
