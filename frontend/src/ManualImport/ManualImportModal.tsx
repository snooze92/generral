// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx
// and InteractiveImportRow.tsx. Each file's series, episodes and quality are picked inline (selects) rather than in
// Sonarr's per-column modals; release group and language editing are not there yet.
import React, { useCallback, useMemo, useState } from 'react';
import { useInvalidateQueue } from 'Activity/Queue/useQueue';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import FormInput from 'Components/Form/FormInput';
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
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { QualityModel } from 'Quality/Quality';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import { CheckInputChanged, InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './ManualImportModal.module.css';

/** The `/manualimport` resource (Sonarr's `ManualImportResource`). */
interface ManualImportItem {
  id: number;
  path: string;
  relativePath: string;
  folderName: string;
  size: number;
  series?: Series;
  episodes: Episode[];
  releaseGroup?: string;
  quality: QualityModel;
  downloadId?: string;
  rejections: { reason: string }[];
  assignable: boolean;
}

interface QualityDefinition {
  quality: { id: number; name: string };
}

/** What the user picked for a file; unset fields keep the suggestion. */
interface Assignment {
  seriesId?: number;
  episodeIds?: number[];
  qualityId?: number;
}

export type ImportMode = 'auto' | 'move' | 'copy';

const columns: Column[] = [
  { name: 'select', label: '', isVisible: true },
  { name: 'relativePath', label: () => translate('RelativePath'), isVisible: true },
  { name: 'series', label: () => translate('Series'), isVisible: true },
  { name: 'episodes', label: () => translate('Episodes'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'rejections', label: '', isVisible: true },
];

const NO_ITEMS: ManualImportItem[] = [];
const NO_EPISODES: Episode[] = [];
const NO_DEFINITIONS: QualityDefinition[] = [];

function episodeLabel(episode: Episode) {
  const number = `${episode.seasonNumber}x${String(episode.episodeNumber).padStart(2, '0')}`;

  return episode.title ? `${number} - ${episode.title}` : number;
}

/** Episodes of the series a row is assigned to (fetched once per series). */
function useSeriesEpisodes(seriesId: number | undefined) {
  const { data } = useApiQuery<Episode[]>({
    path: '/episode',
    queryParams: { seriesId: seriesId ?? 0 },
    queryOptions: { enabled: !!seriesId },
  });

  return (data as Episode[] | undefined) ?? NO_EPISODES;
}

interface RowProps {
  item: ManualImportItem;
  assignment: Assignment;
  isSelected: boolean;
  seriesOptions: { key: number; value: string }[];
  qualityOptions: { key: number; value: string }[];
  onSelectedChange: (id: number, value: boolean) => void;
  onAssignmentChange: (id: number, assignment: Assignment) => void;
}

function ManualImportRow({
  item,
  assignment,
  isSelected,
  seriesOptions,
  qualityOptions,
  onSelectedChange,
  onAssignmentChange,
}: RowProps) {
  const seriesId = assignment.seriesId ?? item.series?.id;
  const episodes = useSeriesEpisodes(seriesId);
  const episodeIds =
    assignment.episodeIds ??
    (seriesId === item.series?.id ? item.episodes.map((e) => e.id) : []);
  const qualityId = assignment.qualityId ?? item.quality.quality.id;
  const isEdited =
    assignment.seriesId !== undefined || assignment.episodeIds !== undefined;
  const isReady = item.assignable && !!seriesId && episodeIds.length > 0;

  const episodeOptions = useMemo(
    () =>
      [...episodes]
        .filter((e) => e.status === 'active')
        .sort(
          (a, b) =>
            b.seasonNumber - a.seasonNumber || a.episodeNumber - b.episodeNumber
        )
        .map((e) => ({ key: e.id, value: episodeLabel(e) })),
    [episodes]
  );

  const handleChange = useCallback(
    ({ name, value }: InputChanged) => {
      if (name === 'seriesId') {
        // A new series starts without episodes.
        onAssignmentChange(item.id, {
          ...assignment,
          seriesId: Number(value),
          episodeIds: [],
        });
      } else if (name === 'episodeIds') {
        onAssignmentChange(item.id, {
          ...assignment,
          episodeIds: value as number[],
        });
      } else if (name === 'qualityId') {
        onAssignmentChange(item.id, { ...assignment, qualityId: Number(value) });
      }
    },
    [item.id, assignment, onAssignmentChange]
  );

  return (
    <TableRow>
      <TableRowCell>
        <CheckInput
          name={`select-${item.id}`}
          ariaLabel={translate('SelectRow')}
          value={isSelected && isReady}
          isDisabled={!isReady}
          onChange={({ value }: CheckInputChanged) =>
            onSelectedChange(item.id, value)
          }
        />
      </TableRowCell>

      <TableRowCell className={styles.path} title={item.path}>
        {item.relativePath}
      </TableRowCell>

      <TableRowCell className={styles.select}>
        {item.assignable ? (
          <FormInput
            type={inputTypes.SELECT}
            name="seriesId"
            values={[
              { key: 0, value: translate('SelectSeries'), isDisabled: true },
              ...seriesOptions,
            ]}
            value={seriesId ?? 0}
            onChange={handleChange}
          />
        ) : (
          item.series?.title
        )}
      </TableRowCell>

      <TableRowCell className={styles.select}>
        {item.assignable && seriesId ? (
          <FormInput
            type={inputTypes.SELECT}
            name="episodeIds"
            values={episodeOptions}
            value={episodeIds}
            onChange={handleChange}
          />
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.quality}>
        <FormInput
          type={inputTypes.SELECT}
          name="qualityId"
          values={qualityOptions}
          value={qualityId}
          isDisabled={!item.assignable}
          onChange={handleChange}
        />
      </TableRowCell>

      <TableRowCell>{formatBytes(item.size)}</TableRowCell>

      <TableRowCell className={styles.rejections}>
        {isEdited && isReady
          ? null
          : item.rejections.map((r) => r.reason).join('; ')}
      </TableRowCell>
    </TableRow>
  );
}

interface ManualImportModalContentProps {
  /** A queued download (the queue's *Manual Import* action)… */
  downloadId?: string;
  /** …or a folder (or file) on disk, optionally suggested against a series. */
  folder?: string;
  seriesId?: number;
  title: string;
  onModalClose: () => void;
}

function ManualImportModalContent({
  downloadId,
  folder,
  seriesId,
  title,
  onModalClose,
}: ManualImportModalContentProps) {
  const { data, isLoading, error } = useApiQuery<ManualImportItem[]>({
    path: '/manualimport',
    queryParams: downloadId ? { downloadId } : { folder: folder ?? '', seriesId },
    // A bad folder is a 400: show it at once instead of retrying.
    queryOptions: { retry: false },
  });
  const items = (data as ManualImportItem[] | undefined) ?? NO_ITEMS;
  const { data: allSeries } = useSeries();
  const { data: definitions } = useApiQuery<QualityDefinition[]>({
    path: '/qualitydefinition',
  });

  const [assignments, setAssignments] = useState<Record<number, Assignment>>({});
  const [selected, setSelected] = useState<number[] | undefined>();
  const [importMode, setImportMode] = useState<ImportMode>(
    downloadId ? 'auto' : 'move'
  );

  // Rows that are importable as suggested start selected.
  const selectedIds = useMemo(
    () =>
      selected ??
      items
        .filter((i) => i.assignable && i.series && i.episodes.length && !i.rejections.length)
        .map((i) => i.id),
    [selected, items]
  );

  const seriesOptions = useMemo(
    () => allSeries.map((s) => ({ key: s.id, value: s.title })),
    [allSeries]
  );
  const qualityOptions = useMemo(
    () =>
      ((definitions as QualityDefinition[] | undefined) ?? NO_DEFINITIONS).map(
        (d) => ({ key: d.quality.id, value: d.quality.name })
      ),
    [definitions]
  );

  const invalidateQueue = useInvalidateQueue();
  const { execute, isExecuting } = useTrackedCommand('/command', () => {
    invalidateQueue();
    onModalClose();
  });

  const handleSelectedChange = useCallback(
    (id: number, value: boolean) =>
      setSelected(
        value ? [...selectedIds, id] : selectedIds.filter((i) => i !== id)
      ),
    [selectedIds]
  );

  const handleAssignmentChange = useCallback(
    (id: number, assignment: Assignment) => {
      setAssignments((current) => ({ ...current, [id]: assignment }));
      if (assignment.episodeIds?.length) {
        setSelected((current) => [...new Set([...(current ?? selectedIds), id])]);
      }
    },
    [selectedIds]
  );

  const files = useMemo(
    () =>
      items
        .filter((item) => selectedIds.includes(item.id) && item.assignable)
        .map((item) => {
          const assignment = assignments[item.id] ?? {};
          const fileSeriesId = assignment.seriesId ?? item.series?.id;
          const episodeIds =
            assignment.episodeIds ??
            (fileSeriesId === item.series?.id ? item.episodes.map((e) => e.id) : []);
          const quality =
            assignment.qualityId === undefined ||
            assignment.qualityId === item.quality.quality.id
              ? item.quality
              : { ...item.quality, quality: { id: assignment.qualityId } };

          return {
            path: item.path,
            folderName: item.folderName,
            seriesId: fileSeriesId,
            episodeIds,
            quality,
            releaseGroup: item.releaseGroup,
            downloadId: item.downloadId,
          };
        })
        .filter((file) => file.seriesId && file.episodeIds.length),
    [items, selectedIds, assignments]
  );

  const handleImportPress = useCallback(() => {
    execute({ name: 'ManualImport', importMode, files });
  }, [files, importMode, execute]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={title}>{translate('ManualImport')}</ModalHeader>

      <ModalBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>
            {translate('ManualImportLoadError')}
            {Array.isArray(error.statusBody)
              ? `: ${(error.statusBody as { errorMessage: string }[])
                  .map((f) => f.errorMessage)
                  .join('; ')}`
              : null}
          </Alert>
        ) : null}

        {!isLoading && !error && !items.length ? (
          <Alert kind={kinds.WARNING}>
            {translate('NoVideoFilesFoundSelectedFolder')}
          </Alert>
        ) : null}

        {items.length ? (
          <Table columns={columns}>
            <TableBody>
              {items.map((item) => (
                <ManualImportRow
                  key={item.id}
                  item={item}
                  assignment={assignments[item.id] ?? {}}
                  isSelected={selectedIds.includes(item.id)}
                  seriesOptions={seriesOptions}
                  qualityOptions={qualityOptions}
                  onSelectedChange={handleSelectedChange}
                  onAssignmentChange={handleAssignmentChange}
                />
              ))}
            </TableBody>
          </Table>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <div className={styles.footer}>
        <div className={styles.importMode}>
          <FormInput
            type={inputTypes.SELECT}
            name="importMode"
            values={[
              ...(downloadId
                ? [{ key: 'auto', value: translate('ImportModeAuto') }]
                : []),
              { key: 'move', value: translate('MoveFiles') },
              { key: 'copy', value: translate('HardlinkCopyFiles') },
            ]}
            value={importMode}
            onChange={({ value }: InputChanged) =>
              setImportMode(value as ImportMode)
            }
          />
        </div>

        <div>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerButton
            kind={kinds.SUCCESS}
            isSpinning={isExecuting}
            isDisabled={!files.length}
            onPress={handleImportPress}
          >
            {translate('Import')}
          </SpinnerButton>
        </div>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

interface ManualImportModalProps extends ManualImportModalContentProps {
  isOpen: boolean;
}

function ManualImportModal({ isOpen, ...props }: ManualImportModalProps) {
  return (
    <Modal
      size={sizes.EXTRA_EXTRA_LARGE}
      isOpen={isOpen}
      onModalClose={props.onModalClose}
    >
      {isOpen ? <ManualImportModalContent {...props} /> : null}
    </Modal>
  );
}

export default ManualImportModal;
