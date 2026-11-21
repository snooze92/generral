// A minimal from-scratch mapping (docs/specs/05-mapping.md § Shape): every event becomes an episode.
const defaultDefinition = `# Seasons are the year of each event, episodes are numbered by start time within it.
season:
  strategy: year
episode:
  strategy: sequential
  order: '{{ event.start }}'
title: '{{ event.title }}'
airDate: '{{ event.start }}'
`;

export default defaultDefinition;
