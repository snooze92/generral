// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/App/App.tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { useState } from 'react';
import {
  createBrowserRouter,
  createRoutesFromElements,
  Outlet,
  Route,
  RouterProvider,
} from 'react-router-dom';
import Page from 'Components/Page/Page';
import ApplyTheme from './ApplyTheme';
import { appRouteElements } from './AppRoutes';
import { queryClient as defaultQueryClient } from './queryClient';

function PageLayout() {
  return (
    <>
      <ApplyTheme />
      <Page>
        <Outlet />
      </Page>
    </>
  );
}

export function createAppRouter() {
  return createBrowserRouter(
    createRoutesFromElements(
      <Route element={<PageLayout />}>{appRouteElements()}</Route>
    ),
    { basename: window.Generral.urlBase || undefined }
  );
}

interface AppProps {
  queryClient?: QueryClient;
}

function App({ queryClient = defaultQueryClient }: AppProps) {
  const [router] = useState(createAppRouter);

  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}

export default App;
