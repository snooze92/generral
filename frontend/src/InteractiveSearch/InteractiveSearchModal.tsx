// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Episode/Search/EpisodeSearchModal.tsx and frontend/src/InteractiveSearch/InteractiveSearch.tsx
import React from 'react';
import Alert from 'Components/Alert';
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
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveSearchRow from './InteractiveSearchRow';
import useReleases from './useReleases';

const columns: Column[] = [
  { name: 'protocol', label: () => translate('Source'), isVisible: true },
  { name: 'age', label: () => translate('Age'), isVisible: true },
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'indexer', label: () => translate('Indexer'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'peers', label: () => translate('Peers'), isVisible: true },
  { name: 'languages', label: () => translate('Languages'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'rejections', label: '', isVisible: true },
  { name: 'download', label: '', isVisible: true },
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
  const { data: releases, isFetching, error } = useReleases(episodeId);

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

        {!isFetching && !error && !releases.length ? (
          <Alert kind={kinds.INFO}>{translate('NoResultsFound')}</Alert>
        ) : null}

        {!isFetching && releases.length ? (
          <Table columns={columns}>
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
