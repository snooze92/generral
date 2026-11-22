// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/InteractiveSearch/InteractiveSearchReleaseRow.tsx and ReleaseActions.tsx
import React from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import EpisodeQuality from 'Episode/EpisodeQuality';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import formatAge from 'Utilities/Number/formatAge';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import Peers from './Peers';
import { Release, useGrabRelease } from './useReleases';
import styles from './InteractiveSearch.module.css';

function getGrabTitle(
  release: Release,
  isGrabbed: boolean,
  grabError?: string
) {
  if (isGrabbed) {
    return translate('AddedToDownloadQueue');
  }

  if (grabError) {
    return grabError;
  }

  return release.approved
    ? translate('AddToDownloadQueue')
    : translate('OverrideAndAddToDownloadQueue');
}

function InteractiveSearchRow({ release }: { release: Release }) {
  const {
    protocol,
    age,
    ageHours,
    ageMinutes,
    title,
    infoUrl,
    indexer,
    size,
    seeders,
    leechers,
    quality,
    rejections,
    downloadAllowed,
  } = release;
  const { grabRelease, isGrabbing, isGrabbed, grabError } =
    useGrabRelease(release);
  const grabTitle = getGrabTitle(
    release,
    isGrabbed,
    grabError ? (grabError.statusBody?.message ?? grabError.message) : undefined
  );

  return (
    <TableRow>
      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={protocol} />
      </TableRowCell>

      <TableRowCell className={styles.age} title={release.publishDate}>
        {formatAge(age, ageHours, ageMinutes)}
      </TableRowCell>

      <TableRowCell className={styles.title}>
        {infoUrl ? <Link to={infoUrl}>{title}</Link> : title}
      </TableRowCell>

      <TableRowCell className={styles.indexer}>{indexer}</TableRowCell>

      <TableRowCell className={styles.size}>{formatBytes(size)}</TableRowCell>

      <TableRowCell className={styles.peers}>
        {protocol === 'torrent' ? (
          <Peers seeders={seeders} leechers={leechers} />
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.quality}>
        <EpisodeQuality quality={quality} showRevision={true} />
      </TableRowCell>

      <TableRowCell className={styles.rejected}>
        {rejections.length ? (
          <Popover
            anchor={<Icon name={icons.DANGER} kind={kinds.DANGER} />}
            accessibleLabel={translate('ReleaseRejected')}
            title={translate('ReleaseRejected')}
            body={
              <ul>
                {rejections.map((rejection) => (
                  <li key={rejection}>{rejection}</li>
                ))}
              </ul>
            }
            position={tooltipPositions.LEFT}
          />
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.download}>
        <SpinnerIconButton
          name={isGrabbed ? icons.DOWNLOADED : icons.DOWNLOAD}
          kind={grabError ? kinds.DANGER : kinds.DEFAULT}
          title={grabTitle}
          aria-label={translate('Grab')}
          isSpinning={isGrabbing}
          isDisabled={!downloadAllowed || isGrabbed}
          onPress={() => grabRelease()}
        />
      </TableRowCell>
    </TableRow>
  );
}

export default InteractiveSearchRow;
