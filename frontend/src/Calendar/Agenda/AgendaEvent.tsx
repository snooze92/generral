// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/Agenda/AgendaEvent.tsx
// Links to the series page (no episode details modal, posters, queue or files yet).
import classNames from 'classnames';
import moment from 'moment-timezone';
import React from 'react';
import getCalendarStatusLabel from 'Calendar/Events/getCalendarStatusLabel';
import getEpisodeLabel from 'Calendar/Events/getEpisodeLabel';
import getStatusStyle from 'Calendar/getStatusStyle';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons, kinds } from 'Helpers/Props';
import { useSingleSeries } from 'Series/useSeries';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import { CalendarItem } from 'typings/Calendar';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatTime from 'Utilities/Date/formatTime';
import translate from 'Utilities/String/translate';
import styles from './AgendaEvent.module.css';

function AgendaEvent(props: CalendarItem) {
  const {
    seriesId,
    title = '',
    seasonNumber,
    episodeNumber,
    airDateUtc,
    runtime,
    monitored,
    hasFile,
  } = props;

  const series = useSingleSeries(seriesId);
  const { longDateFormat, timeFormat } = useUiSettingsValues();

  if (!series) {
    return null;
  }

  const startTime = moment(airDateUtc);
  const endTime = moment(airDateUtc).add(runtime, 'minutes');
  const isMonitored = series.monitored && monitored;
  const statusStyle = getStatusStyle(
    hasFile,
    false,
    startTime,
    endTime,
    isMonitored
  );
  const episode = getEpisodeLabel(series.seriesType, seasonNumber, episodeNumber);

  const eventLabel = translate('CalendarOpenEpisodeDetails', {
    series: series.title,
    episode,
    title,
    airDate: formatDateTime(airDateUtc, longDateFormat, timeFormat),
    status: getCalendarStatusLabel(statusStyle),
  });

  return (
    <div className={classNames(styles.event, styles[statusStyle])}>
      <Link
        className={styles.underlay}
        to={`/series/${series.titleSlug}`}
        aria-label={eventLabel}
      />

      <div className={styles.overlay}>
        <div className={classNames(styles.eventWrapper, styles[statusStyle])}>
          <div className={styles.time}>
            {formatTime(airDateUtc, timeFormat)} -{' '}
            {formatTime(endTime.toISOString(), timeFormat, {
              includeMinuteZero: true,
            })}
          </div>

          <div className={styles.seriesTitle}>{series.title}</div>

          {episode ? (
            <div className={styles.seasonEpisodeNumber}>
              {episode}
              <div className={styles.episodeSeparator}> - </div>
            </div>
          ) : null}

          <div className={styles.episodeTitle}>{title}</div>

          {episodeNumber === 1 && seasonNumber > 0 ? (
            <Icon
              className={styles.statusIcon}
              name={icons.PREMIERE}
              kind={kinds.INFO}
              title={translate('SeasonPremiere')}
            />
          ) : null}
        </div>
      </div>
    </div>
  );
}

export default AgendaEvent;
