import { SeriesType } from 'Series/Series';
import padNumber from 'Utilities/Number/padNumber';

/** `S2026E02` like the iCal feed (Sonarr's `1x02` reads badly with year seasons); none for daily series. */
function getEpisodeLabel(
  seriesType: SeriesType,
  seasonNumber: number,
  episodeNumber: number
) {
  if (seriesType === 'daily') {
    return '';
  }

  return `S${padNumber(seasonNumber, 2)}E${padNumber(episodeNumber, 2)}`;
}

export default getEpisodeLabel;
