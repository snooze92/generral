import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Series from 'Series/Series';
import { mockApi, uiSettings } from 'test/mockApi';
import { CalendarItem } from 'typings/Calendar';
import { setCalendarOptions } from './calendarOptionsStore';
import CalendarPage from './CalendarPage';

const series = {
  id: 1,
  title: 'Formula 1',
  titleSlug: 'formula-1',
  seriesType: 'standard',
  monitored: true,
  tags: [],
  seasons: [],
} as unknown as Series;

function episode(changes: Partial<CalendarItem>): CalendarItem {
  return {
    id: 11,
    seriesId: 1,
    episodeFileId: 0,
    seasonNumber: 2026,
    episodeNumber: 2,
    title: 'Monaco Grand Prix',
    airDate: '2026-11-22',
    airDateUtc: '2026-11-22T14:00:00Z',
    runtime: 120,
    hasFile: false,
    monitored: true,
    numberLocked: false,
    source: 'mapping',
    status: 'active',
    links: [],
    ...changes,
  };
}

function renderCalendar(calendar: CalendarItem[]) {
  const fetchMock = mockApi({
    'GET /api/v1/series': [series],
    'GET /api/v1/tag': [{ id: 3, label: 'motorsport' }],
    'GET /api/v1/calendar': calendar,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <CalendarPage />
      </MemoryRouter>
    </QueryClientProvider>
  );

  return fetchMock;
}

function calendarRequests(fetchMock: ReturnType<typeof mockApi>) {
  return fetchMock.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === '/api/v1/calendar');
}

describe('CalendarPage', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-21T12:00:00Z') });
    setCalendarOptions({ view: 'month', selectedFilterKey: 'monitored' });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows the month with events linking to the series and navigates months', async () => {
    const fetchMock = renderCalendar([episode({})]);

    expect(await screen.findByText('November 2026')).toBeTruthy();
    expect(await screen.findByText('Monaco Grand Prix')).toBeTruthy();
    expect(screen.getByText('S2026E02')).toBeTruthy();

    const link = screen.getByLabelText(/^Open details for Formula 1/);
    expect(link.getAttribute('href')).toBe('/series/formula-1');

    const [first] = calendarRequests(fetchMock);
    expect(first.searchParams.get('unmonitored')).toBe('false');
    // Month grid: Monday 26 Oct to Sunday 6 Dec (firstDayOfWeek = 1).
    expect(new Date(first.searchParams.get('start')!).getDate()).toBe(26);

    fireEvent.click(screen.getByLabelText('Go to next month'));
    expect(await screen.findByText('December 2026')).toBeTruthy();
  });

  it('includes unmonitored episodes with the All filter and styles them', async () => {
    const fetchMock = renderCalendar([episode({ monitored: false })]);

    await screen.findByText('Monaco Grand Prix');
    const event = screen.getByLabelText(/^Open details for Formula 1/)
      .parentElement!;
    expect(event.className).toMatch(/unmonitored/);

    fireEvent.click(screen.getByText('Filter'));
    fireEvent.click(await screen.findByText('All'));

    await waitFor(() =>
      expect(
        calendarRequests(fetchMock).some(
          (url) => url.searchParams.get('unmonitored') === 'true'
        )
      ).toBe(true)
    );
  });

  it('lists events by day in the agenda view', async () => {
    renderCalendar([
      episode({}),
      episode({
        id: 12,
        episodeNumber: 3,
        title: 'Las Vegas Grand Prix',
        airDateUtc: '2026-11-29T14:00:00Z',
      }),
    ]);

    fireEvent.click(await screen.findByText('Agenda'));

    expect(await screen.findByText('Las Vegas Grand Prix')).toBeTruthy();
    expect(screen.getByText('Sunday, 22 November 2026')).toBeTruthy();
    expect(screen.getByText('Sunday, 29 November 2026')).toBeTruthy();
  });

  it('builds the iCal feed URL from the options', async () => {
    renderCalendar([]);

    fireEvent.click(await screen.findByText('iCal Link'));

    const feed = (await screen.findByDisplayValue(
      /generral\.ics/
    )) as HTMLInputElement;
    expect(feed.value).toBe(
      'http://localhost:3000/feed/v1/calendar/generral.ics?apikey=test-api-key'
    );

    fireEvent.click(
      screen.getByRole('checkbox', { name: /Include Unmonitored/ })
    );
    fireEvent.change(screen.getByDisplayValue('28'), {
      target: { value: '60' },
    });
    fireEvent.blur(screen.getByDisplayValue('60'));

    await waitFor(() =>
      expect(feed.value).toBe(
        'http://localhost:3000/feed/v1/calendar/generral.ics?unmonitored=true&futureDays=60&apikey=test-api-key'
      )
    );
  });
});
