// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/Details/SeriesDetailsSeason.tsx
import React, { useCallback, useMemo, useRef } from 'react';
import { useQueueDetails } from 'Activity/Queue/useQueue';
import useSearchCommand from 'Commands/useSearchCommand';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import Popover from 'Components/Tooltip/Popover';
import Episode from 'Episode/Episode';
import {
  useSeasonEpisodes,
  useToggleEpisodesMonitored,
} from 'Episode/useEpisodes';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import { Season } from 'Series/Series';
import { useToggleSeasonMonitored } from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import EpisodeRow from './EpisodeRow';
import SeasonInfo from './SeasonInfo';
import styles from './SeriesDetailsSeason.module.css';

const columns: Column[] = [
  { name: 'monitored', label: '', isVisible: true },
  { name: 'episodeNumber', label: '#', isVisible: true },
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'airDateUtc', label: () => translate('AirDate'), isVisible: true },
  { name: 'status', label: () => translate('Status'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

export function getSeasonTitle(seasonNumber: number) {
  return seasonNumber === 0
    ? translate('Specials')
    : translate('SeasonNumberToken', { seasonNumber });
}

function getEpisodeCountKind(
  monitored: boolean,
  episodeFileCount: number,
  episodeCount: number
) {
  if (episodeFileCount === episodeCount && episodeCount > 0) {
    return kinds.SUCCESS;
  }

  if (!monitored) {
    return kinds.WARNING;
  }

  if (episodeCount === 0) {
    return kinds.DEFAULT;
  }

  return kinds.DANGER;
}

interface SeriesDetailsSeasonProps extends Season {
  seriesId: number;
  seriesMonitored: boolean;
  isExpanded?: boolean;
  onExpandPress: (seasonNumber: number, isExpanded: boolean) => void;
}

function SeriesDetailsSeason({
  seriesId,
  seriesMonitored,
  seasonNumber,
  title,
  monitored,
  statistics,
  isExpanded = false,
  onExpandPress,
}: SeriesDetailsSeasonProps) {
  const { data: episodes } = useSeasonEpisodes(seriesId, seasonNumber);
  const { toggleSeasonMonitored, isTogglingSeasonMonitored } =
    useToggleSeasonMonitored(seriesId);
  const { toggleEpisodesMonitored, togglingEpisodeIds } =
    useToggleEpisodesMonitored();
  const lastToggledEpisode = useRef<number | null>(null);
  const { search, isSearching } = useSearchCommand();
  const queue = useQueueDetails();
  const queueByEpisode = useMemo(
    () =>
      new Map(
        queue.flatMap((item) =>
          item.episodeIds.map((episodeId) => [episodeId, item] as const)
        )
      ),
    [queue]
  );

  const {
    episodeCount = 0,
    episodeFileCount = 0,
    totalEpisodeCount = 0,
    sizeOnDisk = 0,
  } = statistics ?? {};

  const monitoredEpisodeCount = useMemo(
    () => episodes.filter((episode) => episode.monitored).length,
    [episodes]
  );

  const seasonNumberTitle = getSeasonTitle(seasonNumber);
  const showEpisodesLabel = translate('ShowEpisodes');
  const hideEpisodesLabel = translate('HideEpisodes');

  const handleMonitorSeasonPress = useCallback(
    (value: boolean) => toggleSeasonMonitored({ seasonNumber, monitored: value }),
    [seasonNumber, toggleSeasonMonitored]
  );

  const handleExpandPress = useCallback(
    () => onExpandPress(seasonNumber, !isExpanded),
    [seasonNumber, isExpanded, onExpandPress]
  );

  // Shift-click toggles the range since the last toggled episode, like Sonarr.
  const handleMonitorEpisodePress = useCallback(
    (
      episodeId: number,
      value: boolean,
      { shiftKey }: { shiftKey: boolean }
    ) => {
      const lastId = lastToggledEpisode.current;
      let episodeIds = [episodeId];

      if (shiftKey && lastId !== null) {
        const from = episodes.findIndex((e) => e.id === lastId);
        const to = episodes.findIndex((e) => e.id === episodeId);

        if (from !== -1 && to !== -1) {
          episodeIds = episodes
            .slice(Math.min(from, to), Math.max(from, to) + 1)
            .map((e) => e.id);
        }
      }

      lastToggledEpisode.current = episodeId;
      toggleEpisodesMonitored({ episodeIds, monitored: value });
    },
    [episodes, toggleEpisodesMonitored]
  );

  const setAllMonitored = useCallback(
    (value: boolean) => {
      const episodeIds = episodes.map((episode: Episode) => episode.id);

      if (episodeIds.length) {
        toggleEpisodesMonitored({ episodeIds, monitored: value });
      }
    },
    [episodes, toggleEpisodesMonitored]
  );

  const monitorSeasonLabel = translate(
    monitored ? 'UnmonitorSeason' : 'MonitorSeason',
    { season: seasonNumberTitle }
  );
  const kind = getEpisodeCountKind(monitored, episodeFileCount, episodeCount);

  return (
    <div className={styles.season}>
      <div className={styles.header}>
        <div className={styles.left}>
          <MonitorToggleButton
            monitored={monitored}
            isDisabled={!seriesMonitored && !monitored}
            isSaving={isTogglingSeasonMonitored}
            size={24}
            title={monitorSeasonLabel}
            onPress={handleMonitorSeasonPress}
          />

          <div className={styles.seasonInfo}>
            <div className={styles.seasonNumber}>{seasonNumberTitle}</div>
            {title ? (
              <div className={styles.seasonTitle} title={title}>
                {title}
              </div>
            ) : null}
          </div>

          <div className={styles.seasonStats}>
            <Popover
              className={styles.episodeCountTooltip}
              canFlip={true}
              anchor={
                <Label
                  className={styles.seasonStatsLabel}
                  kind={kind}
                  size={sizes.LARGE}
                >
                  <span>{`${episodeFileCount} / ${episodeCount}`}</span>
                </Label>
              }
              title={translate('SeasonInformation')}
              body={
                <SeasonInfo
                  totalEpisodeCount={totalEpisodeCount}
                  monitoredEpisodeCount={monitoredEpisodeCount}
                  episodeFileCount={episodeFileCount}
                  sizeOnDisk={sizeOnDisk}
                />
              }
              position={tooltipPositions.BOTTOM}
            />
          </div>
        </div>

        <Link
          className={styles.expandButton}
          aria-label={isExpanded ? hideEpisodesLabel : showEpisodesLabel}
          onPress={handleExpandPress}
        >
          <Icon
            className={styles.expandButtonIcon}
            name={isExpanded ? icons.COLLAPSE : icons.EXPAND}
            title={isExpanded ? hideEpisodesLabel : showEpisodesLabel}
            size={24}
          />
        </Link>

        <div className={styles.actions}>
          <SpinnerIconButton
            className={styles.actionButton}
            name={icons.SEARCH}
            title={translate('SearchForMonitoredEpisodesSeason')}
            aria-label={translate('SearchForMonitoredEpisodesSeason')}
            size={20}
            isSpinning={isSearching}
            isDisabled={!seriesMonitored || !monitored}
            onPress={() =>
              search({ name: 'SeasonSearch', seriesId, seasonNumber })
            }
          />

          <Menu className={styles.actionsMenu} alignMenu="right">
            <MenuButton
              className={styles.actionsMenuButton}
              aria-label={translate('EpisodeMonitoring')}
            >
              <Icon name={icons.MONITORED} size={20} />
            </MenuButton>

            <MenuContent className={styles.actionsMenuContent}>
              <MenuItem
                isDisabled={!episodes.length}
                onPress={() => setAllMonitored(true)}
              >
                <Icon className={styles.actionMenuIcon} name={icons.MONITORED} />
                {translate('MonitorAllEpisodes')}
              </MenuItem>

              <MenuItem
                isDisabled={!episodes.length}
                onPress={() => setAllMonitored(false)}
              >
                <Icon
                  className={styles.actionMenuIcon}
                  name={icons.UNMONITORED}
                />
                {translate('UnmonitorAllEpisodes')}
              </MenuItem>
            </MenuContent>
          </Menu>
        </div>
      </div>

      {isExpanded ? (
        <div className={styles.episodes}>
          {episodes.length ? (
            <Table columns={columns}>
              <TableBody>
                {episodes.map((episode) => (
                  <EpisodeRow
                    key={episode.id}
                    episode={episode}
                    queueItem={queueByEpisode.get(episode.id)}
                    seriesMonitored={seriesMonitored}
                    isSaving={togglingEpisodeIds.includes(episode.id)}
                    onMonitorEpisodePress={handleMonitorEpisodePress}
                  />
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className={styles.noEpisodes}>
              {translate('NoEpisodesInThisSeason')}
            </div>
          )}

          <div className={styles.collapseButtonContainer}>
            <IconButton
              iconClassName={styles.collapseButtonIcon}
              name={icons.COLLAPSE}
              size={20}
              title={hideEpisodesLabel}
              aria-label={hideEpisodesLabel}
              onPress={handleExpandPress}
            />
          </div>
        </div>
      ) : null}
    </div>
  );
}

export default SeriesDetailsSeason;
