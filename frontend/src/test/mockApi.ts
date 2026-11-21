import { vi } from 'vitest';

export const systemStatus = {
  appName: 'Generral',
  instanceName: 'Generral',
  version: '0.1.0',
  commit: 'abc1234',
  buildTime: '2026-11-20T00:00:00Z',
  isDebug: false,
  isProduction: true,
  isContainerized: false,
  isLinux: true,
  isOsx: false,
  isWindows: false,
  osName: 'linux',
  osVersion: '6.0',
  mode: 'console',
  authentication: 'forms',
  databaseType: 'sqLite',
  databaseVersion: '3.46.0',
  migrationVersion: '20261120_Initial',
  urlBase: '',
  runtimeName: '.NET',
  runtimeVersion: '10.0.0',
  startTime: '2026-11-20T00:00:00Z',
  startupPath: '/app',
  appData: '/config',
};

export const uiSettings = {
  firstDayOfWeek: 1,
  calendarWeekColumnHeader: 'ddd D/M',
  shortDateFormat: 'DD MMM YYYY',
  longDateFormat: 'dddd, D MMMM YYYY',
  timeFormat: 'HH:mm',
  timeZone: '',
  showRelativeDates: true,
  enableColorImpairedMode: false,
  headerColor: 'fullColor',
  theme: 'light',
  spoilerFreeMode: true,
};

export const generalSettings = {
  bindAddress: '*',
  port: 8990,
  urlBase: '',
  instanceName: 'Generral',
  authenticationMethod: 'forms',
  authenticationRequired: 'enabled',
  username: 'admin',
  password: '',
  passwordConfirmation: '',
  apiKey: 'test-api-key',
  logLevel: 'info',
  allowScriptSources: false,
  overriddenByEnvironment: ['port'],
};

const responses: Record<string, unknown> = {
  '/api/v1/system/status': systemStatus,
  '/api/v1/settings/ui': uiSettings,
  '/api/v1/settings/general': generalSettings,
};

export interface MockRequest {
  method: string;
  url: URL;
  body: unknown;
}

/** A canned response, or a handler returning the body or a Response. */
export type MockRoute = unknown | ((request: MockRequest) => unknown);

/**
 * Replaces global fetch with canned API responses; unknown paths return 404. `routes` are keyed
 * by path (any method) or by "METHOD path".
 */
export function mockApi(routes: Record<string, MockRoute> = {}) {
  const fetchMock = vi.fn(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      const method = init?.method ?? 'GET';
      const route =
        routes[`${method} ${url.pathname}`] ??
        routes[url.pathname] ??
        (method === 'GET' ? responses[url.pathname] : undefined);

      const result =
        typeof route === 'function'
          ? route({
              method,
              url,
              body: init?.body ? JSON.parse(String(init.body)) : undefined,
            })
          : route;

      if (result instanceof Response) {
        return result;
      }

      return new Response(
        result !== undefined ? JSON.stringify(result) : '{}',
        {
          status: result !== undefined ? 200 : 404,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    }
  );

  vi.stubGlobal('fetch', fetchMock);

  return fetchMock;
}
