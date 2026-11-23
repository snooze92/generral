// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Episode/Search/EpisodeSearchModal.tsx, frontend/src/InteractiveSearch/InteractiveSearch.tsx
// and InteractiveSearchColumnsHeader.tsx
import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds, sizes } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import translate from 'Utilities/String/translate';
import InteractiveSearchRow from './InteractiveSearchRow';
import useReleases, {
  FIXED_SORT_DIRECTIONS,
  setReleaseSort,
} from './useReleases';

// Column names are the sort keys (Sonarr sorts quality by `qualityWeight`).
const columns: Column[] = [
  {
    name: 'protocol',
    label: () => translate('Source'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'age',
    label: () => translate('Age'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'title',
    label: () => translate('Title'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'indexer',
    label: () => translate('Indexer'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'peers',
    label: () => translate('Peers'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'qualityWeight',
    label: () => translate('Quality'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'rejections',
    label: <Icon name={icons.DANGER} title={translate('Rejections')} />,
    columnLabel: () => translate('Rejections'),
    isSortable: true,
    fixedSortDirection: 'ascending',
    isVisible: true,
  },
  {
    name: 'releaseWeight',
    label: <Icon name={icons.DOWNLOAD} title={translate('Preferred')} />,
    columnLabel: () => translate('Preferred'),
    isSortable: true,
    fixedSortDirection: 'ascending',
    isVisible: true,
  },
];

interface InteractiveSearchContentProps {
  episodeId: number;
  title: string;
  onModalClose: () => void;
}

function InteractiveSearchContent({
  episodeId,
  title,
  onModalClose,
}: InteractiveSearchContentProps) {
  const {
    data: releases,
    isFetching,
    error,
    timedOutIndexers,
    sortKey,
    sortDirection,
  } = useReleases(episodeId);

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      setReleaseSort(name, direction ?? FIXED_SORT_DIRECTIONS[name]);
    },
    []
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={title}>
        {translate('InteractiveSearchModalHeader')}
      </ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>
            {translate('EpisodeSearchResultsLoadError')}
          </Alert>
        ) : null}

        {!isFetching && timedOutIndexers.length ? (
          <Alert kind={kinds.WARNING}>
            {translate('InteractiveSearchIndexersTimedOut', {
              indexerNames: timedOutIndexers.join(', '),
            })}
          </Alert>
        ) : null}

        {!isFetching && !error && !releases.length ? (
          <Alert kind={kinds.INFO}>{translate('NoResultsFound')}</Alert>
        ) : null}

        {!isFetching && releases.length ? (
          <Table
            columns={columns}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortPress={handleSortPress}
          >
            <TableBody>
              {releases.map((release) => (
                <InteractiveSearchRow
                  key={`${release.indexerId}-${release.guid}`}
                  release={release}
                />
              ))}
            </TableBody>
          </Table>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

interface InteractiveSearchModalProps extends InteractiveSearchContentProps {
  isOpen: boolean;
}

function InteractiveSearchModal({
  isOpen,
  onModalClose,
  ...otherProps
}: InteractiveSearchModalProps) {
  return (
    <Modal
      size={sizes.EXTRA_EXTRA_LARGE}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <InteractiveSearchContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default InteractiveSearchModal;
