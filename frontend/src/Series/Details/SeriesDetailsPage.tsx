// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/Details/SeriesDetailsPage.tsx
import React, { useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import SeriesDetails from './SeriesDetails';

function SeriesDetailsPage() {
  const { data: allSeries, isLoading, error } = useSeries();
  const { titleSlug } = useParams<{ titleSlug: string }>();

  const navigate = useNavigate();

  const series = allSeries.find((s) => s.titleSlug === titleSlug);
  const previousSeriesId = usePrevious(series?.id);
  const previousTitleSlug = usePrevious(titleSlug);

  // The series was deleted while shown.
  useEffect(() => {
    if (!series && previousSeriesId !== undefined && titleSlug === previousTitleSlug) {
      navigate('/');
    }
  }, [series, previousSeriesId, titleSlug, previousTitleSlug, navigate]);

  if (isLoading || error) {
    return (
      <PageContent title={translate('Series')}>
        <PageContentBody>
          {isLoading ? (
            <LoadingIndicator />
          ) : (
            <Alert kind={kinds.DANGER}>{translate('SeriesLoadError')}</Alert>
          )}
        </PageContentBody>
      </PageContent>
    );
  }

  if (!series) {
    return <NotFound message={translate('SeriesCannotBeFound')} />;
  }

  // Keyed so that the season expansion resets when navigating to another series.
  return <SeriesDetails key={series.id} series={series} />;
}

export default SeriesDetailsPage;
