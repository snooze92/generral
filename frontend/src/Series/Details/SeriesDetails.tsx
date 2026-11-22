// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/Details/SeriesDetails.tsx
import React, { useCallback, useMemo, useState } from 'react';
import useSearchCommand from 'Commands/useSearchCommand';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSpacer from 'Components/Page/Toolbar/PageToolbarSpacer';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import SeriesTagList from 'Components/SeriesTagList';
import useEpisodes from 'Episode/useEpisodes';
import { icons, kinds, sizes } from 'Helpers/Props';
import DeleteSeriesModal from 'Series/Delete/DeleteSeriesModal';
import Series from 'Series/Series';
import { getSeriesStatusDetails } from 'Series/SeriesStatus';
import { useToggleSeriesMonitored } from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import SeriesDetailsPath from './SeriesDetailsPath';
import SeriesDetailsSeason from './SeriesDetailsSeason';
import styles from './SeriesDetails.module.css';

interface SeriesDetailsProps {
  series: Series;
}

function SeriesDetails({ series }: SeriesDetailsProps) {
  const {
    id,
    title,
    overview,
    network,
    path,
    tags,
    monitored,
    status,
    seriesType,
    seasons,
    images,
  } = series;

  const { isLoading, error } = useEpisodes(id);
  const { toggleSeriesMonitored, isTogglingSeriesMonitored } =
    useToggleSeriesMonitored(id);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const { search, isSearching } = useSearchCommand();

  // The latest season starts expanded, like Sonarr.
  const sortedSeasons = useMemo(
    () => [...seasons].sort((a, b) => b.seasonNumber - a.seasonNumber),
    [seasons]
  );
  const [expanded, setExpanded] = useState<Record<number, boolean>>(() =>
    sortedSeasons.length ? { [sortedSeasons[0].seasonNumber]: true } : {}
  );
  const allExpanded =
    !!sortedSeasons.length &&
    sortedSeasons.every((season) => expanded[season.seasonNumber]);

  const statusDetails = getSeriesStatusDetails(status);
  const fanartUrl = images?.find((image) => image.coverType === 'fanart')?.url;

  const handleMonitorToggle = useCallback(
    () => toggleSeriesMonitored(!monitored),
    [monitored, toggleSeriesMonitored]
  );

  const handleExpandPress = useCallback(
    (seasonNumber: number, isExpanded: boolean) =>
      setExpanded((current) => ({ ...current, [seasonNumber]: isExpanded })),
    []
  );

  const handleExpandAllPress = useCallback(() => {
    setExpanded(
      Object.fromEntries(
        sortedSeasons.map((season) => [season.seasonNumber, !allExpanded])
      )
    );
  }, [sortedSeasons, allExpanded]);

  return (
    <PageContent title={title}>
      <PageToolbar>
        <ToolbarItem
          id="search-monitored"
          priority={1}
          groupId="left"
          label={translate('SearchMonitored')}
          iconName={icons.SEARCH}
          isSpinning={isSearching}
          isDisabled={!monitored}
          onPress={() => search({ name: 'SeriesSearch', seriesId: id })}
        />

        <ToolbarItem
          id="delete"
          priority={1}
          groupId="left"
          label={translate('Delete')}
          iconName={icons.DELETE}
          onPress={() => setIsDeleteOpen(true)}
        />

        <PageToolbarSpacer />

        <ToolbarItem
          id="expand-all"
          priority={2}
          groupId="right"
          label={allExpanded ? translate('CollapseAll') : translate('ExpandAll')}
          iconName={allExpanded ? icons.COLLAPSE : icons.EXPAND}
          isDisabled={!sortedSeasons.length}
          onPress={handleExpandAllPress}
        />
      </PageToolbar>

      <PageContentBody innerClassName={styles.innerContentBody}>
        <div className={styles.header}>
          <div
            className={styles.backdrop}
            style={
              fanartUrl ? { backgroundImage: `url(${fanartUrl})` } : undefined
            }
          >
            <div className={styles.backdropOverlay} />
          </div>

          <div className={styles.headerContent}>
            <div className={styles.info}>
              <div className={styles.titleRow}>
                <div className={styles.titleContainer}>
                  <div className={styles.title}>{title}</div>
                </div>
              </div>

              <div className={styles.detailsChips}>
                <div className={styles.chipsRow}>
                  <Link
                    className={styles.monitoredToggle}
                    isDisabled={isTogglingSeriesMonitored}
                    aria-pressed={monitored}
                    onPress={handleMonitorToggle}
                  >
                    <Label
                      className={styles.detailsLabel}
                      size={sizes.LARGE}
                      icon={icons.MONITORED}
                      iconFilled={monitored}
                      interactive={true}
                    >
                      <span className={styles.monitoredText}>
                        {monitored
                          ? translate('Monitored')
                          : translate('Unmonitored')}
                      </span>
                    </Label>
                  </Link>

                  <Label
                    className={styles.detailsLabel}
                    title={translate('SeriesType')}
                    size={sizes.LARGE}
                    icon={icons.SEASON_TYPE}
                  >
                    <span className={styles.seasonTypeName}>
                      {seriesType === 'daily'
                        ? translate('Daily')
                        : translate('Standard')}
                    </span>
                  </Label>

                  <Label
                    className={styles.detailsLabel}
                    title={statusDetails.message}
                    size={sizes.LARGE}
                    icon={statusDetails.icon}
                  >
                    <span className={styles.statusName}>
                      {statusDetails.title}
                    </span>
                  </Label>

                  {network ? (
                    <Label
                      className={styles.detailsLabel}
                      title={translate('Network')}
                      size={sizes.LARGE}
                      icon={icons.NETWORK}
                    >
                      <span className={styles.network}>{network}</span>
                    </Label>
                  ) : null}
                </div>

                {tags.length ? (
                  <div className={styles.chipsRow}>
                    <SeriesTagList tags={tags} />
                  </div>
                ) : null}
              </div>

              <div className={styles.heroFooter}>
                <SeriesDetailsPath path={path} />
              </div>
            </div>
          </div>
        </div>

        <div className={styles.contentContainer}>
          {overview ? (
            <section className={styles.overviewSection}>
              <div className={styles.overviewFrame} aria-hidden="true">
                <span className={styles.overviewLabel}>
                  {translate('Overview')}
                </span>
              </div>

              <p className={styles.overview}>{overview}</p>
            </section>
          ) : null}

          {isLoading ? <LoadingIndicator /> : null}

          {error ? (
            <Alert kind={kinds.DANGER}>{translate('EpisodesLoadError')}</Alert>
          ) : null}

          {!isLoading && !error && sortedSeasons.length
            ? sortedSeasons.map((season) => (
                <SeriesDetailsSeason
                  key={season.seasonNumber}
                  seriesId={id}
                  seriesMonitored={monitored}
                  {...season}
                  isExpanded={expanded[season.seasonNumber]}
                  onExpandPress={handleExpandPress}
                />
              ))
            : null}

          {!isLoading && !error && !sortedSeasons.length ? (
            <Alert kind={kinds.WARNING}>
              {translate('NoEpisodeInformation')}
            </Alert>
          ) : null}
        </div>

        <DeleteSeriesModal
          isOpen={isDeleteOpen}
          series={series}
          onModalClose={() => setIsDeleteOpen(false)}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default SeriesDetails;
