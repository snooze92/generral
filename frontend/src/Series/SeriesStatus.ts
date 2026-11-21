// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/SeriesStatus.ts
import { icons } from 'Helpers/Props';
import { SeriesStatus } from 'Series/Series';
import translate from 'Utilities/String/translate';

export function getSeriesStatusDetails(status: SeriesStatus) {
  if (status === 'ended') {
    return {
      icon: icons.SERIES_ENDED,
      title: translate('Ended'),
      message: translate('EndedSeriesDescription'),
    };
  }

  return {
    icon: icons.SERIES_CONTINUING,
    title: translate('Continuing'),
    message: translate('ContinuingSeriesDescription'),
  };
}
