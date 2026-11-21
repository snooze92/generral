import Series from 'Series/Series';

/** Slugs are unique (docs/specs/11-api.md), so unlike Sonarr the id isn't part of the path. */
function getSeriesPath({ titleSlug }: Pick<Series, 'titleSlug'>) {
  return `/series/${titleSlug}`;
}

export default getSeriesPath;
