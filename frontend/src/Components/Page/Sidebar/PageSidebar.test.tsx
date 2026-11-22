import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import PageSidebar from './PageSidebar';

function renderSidebar(path: string) {
  // The System item shows the health issue count.
  const queryClient = new QueryClient();
  queryClient.setQueryData(['/health'], []);

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <PageSidebar />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe('PageSidebar', () => {
  it('renders the Generral sections', () => {
    renderSidebar('/series');

    for (const section of [
      'Series',
      'Sources',
      'Calendar',
      'Activity',
      'Wanted',
      'Settings',
      'System',
    ]) {
      expect(screen.getByText(section)).toBeTruthy();
    }

    expect(screen.queryByText('Statistics')).toBeNull();
  });

  it('expands the children of the active section', () => {
    renderSidebar('/settings/general');

    expect(screen.getByText('General')).toBeTruthy();
    expect(screen.getByText('Download Clients')).toBeTruthy();
    expect(screen.queryByText('Custom Formats')).toBeNull();
    expect(screen.queryByText('Cutoff Unmet')).toBeNull();
  });
});
