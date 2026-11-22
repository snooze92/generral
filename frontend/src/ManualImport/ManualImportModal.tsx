// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx
// Read-only assignments: the files of a queued download with the episodes Generral suggests; the
// importable ones can be imported (no series/episode/quality editing yet).
import React, { useCallback, useMemo, useState } from 'react';
import { useInvalidateQueue } from 'Activity/Queue/useQueue';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import Episode from 'Episode/Episode';
import EpisodeQuality from 'Episode/EpisodeQuality';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds, sizes } from 'Helpers/Props';
import { QualityModel } from 'Quality/Quality';
import Series from 'Series/Series';
import { CheckInputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

/** The `/manualimport` resource (Sonarr's `ManualImportResource`). */
interface ManualImportItem {
  id: number;
  path: string;
  relativePath: string;
  size: number;
  series?: Series;
  episodes: Episode[];
  releaseGroup?: string;
  quality: QualityModel;
  downloadId?: string;
  rejections: { reason: string }[];
}

const columns: Column[] = [
  { name: 'select', label: '', isVisible: true },
  { name: 'relativePath', label: () => translate('RelativePath'), isVisible: true },
  { name: 'episodes', label: () => translate('Episodes'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'rejections', label: '', isVisible: true },
];

const importable = (item: ManualImportItem) =>
  !!item.series && item.episodes.length > 0 && !item.rejections.length;

interface ManualImportModalContentProps {
  downloadId: string;
  title: string;
  onModalClose: () => void;
}

function ManualImportModalContent({
  downloadId,
  title,
  onModalClose,
}: ManualImportModalContentProps) {
  const { data = [], isLoading, error } = useApiQuery<ManualImportItem[]>({
    path: '/manualimport',
    queryParams: { downloadId },
  });
  const [selected, setSelected] = useState<number[] | undefined>();
  const selectedIds = useMemo(
    () => selected ?? data.filter(importable).map((item) => item.id),
    [selected, data]
  );

  const invalidateQueue = useInvalidateQueue();
  const { execute, isExecuting } = useTrackedCommand('/command', () => {
    invalidateQueue();
    onModalClose();
  });

  const handleSelectedChange = useCallback(
    (id: number, value: boolean) =>
      setSelected(
        value
          ? [...selectedIds, id]
          : selectedIds.filter((i) => i !== id)
      ),
    [selectedIds]
  );

  const handleImportPress = useCallback(() => {
    execute({
      name: 'ManualImport',
      importMode: 'auto',
      files: data
        .filter((item) => selectedIds.includes(item.id))
        .map((item) => ({
          path: item.path,
          seriesId: item.series!.id,
          episodeIds: item.episodes.map((e) => e.id),
          quality: item.quality,
          releaseGroup: item.releaseGroup,
          downloadId: item.downloadId,
        })),
    });
  }, [data, selectedIds, execute]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={title}>{translate('ManualImport')}</ModalHeader>

      <ModalBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('ManualImportLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !data.length ? (
          <Alert kind={kinds.WARNING}>{translate('NoVideoFilesFoundSelectedFolder')}</Alert>
        ) : null}

        {data.length ? (
          <Table columns={columns}>
            <TableBody>
              {data.map((item) => (
                <TableRow key={item.id}>
                  <TableRowCell>
                    <CheckInput
                      name={`select-${item.id}`}
                      ariaLabel={translate('SelectRow')}
                      value={selectedIds.includes(item.id)}
                      isDisabled={!importable(item)}
                      onChange={({ value }: CheckInputChanged) =>
                        handleSelectedChange(item.id, value)
                      }
                    />
                  </TableRowCell>
                  <TableRowCell>{item.relativePath}</TableRowCell>
                  <TableRowCell>
                    {item.episodes
                      .map((e) => `${e.seasonNumber}x${e.episodeNumber}`)
                      .join(', ')}
                  </TableRowCell>
                  <TableRowCell>
                    <EpisodeQuality quality={item.quality} />
                  </TableRowCell>
                  <TableRowCell>{formatBytes(item.size)}</TableRowCell>
                  <TableRowCell>
                    {item.rejections.map((r) => r.reason).join('; ')}
                  </TableRowCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.SUCCESS}
          isSpinning={isExecuting}
          isDisabled={!selectedIds.length}
          onPress={handleImportPress}
        >
          {translate('Import')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

interface ManualImportModalProps extends ManualImportModalContentProps {
  isOpen: boolean;
}

function ManualImportModal({ isOpen, ...props }: ManualImportModalProps) {
  return (
    <Modal size={sizes.EXTRA_LARGE} isOpen={isOpen} onModalClose={props.onModalClose}>
      {isOpen ? <ManualImportModalContent {...props} /> : null}
    </Modal>
  );
}

export default ManualImportModal;
