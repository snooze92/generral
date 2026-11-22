// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/RootFolder/RootFolders.tsx and
// frontend/src/Settings/MediaManagement/RootFolder/AddRootFolder.tsx (a typed path instead of the
// file browser).
import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import FormInput from 'Components/Form/FormInput';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageSectionContent from 'Components/Page/PageSectionContent';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiMutation, {
  getValidationFailures,
} from 'Helpers/Hooks/useApiMutation';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import { useDeleteProvider } from 'Settings/Provider/useProviders';
import { InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { ROOT_FOLDER_PATH, RootFolder, useRootFolders } from './MediaManagement';
import styles from './MediaManagementSettings.module.css';

const columns: Column[] = [
  { name: 'path', label: () => translate('Path'), isVisible: true },
  { name: 'freeSpace', label: () => translate('FreeSpace'), isVisible: true },
  {
    name: 'unmappedFolders',
    label: () => translate('UnmappedFolders'),
    isVisible: true,
  },
  { name: 'actions', label: '', isVisible: true },
];

function RootFolderRow({ rootFolder }: { rootFolder: RootFolder }) {
  const { id, path, accessible, freeSpace, unmappedFolders } = rootFolder;
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const { deleteProvider, isDeleting } = useDeleteProvider<RootFolder>(
    ROOT_FOLDER_PATH,
    id
  );

  return (
    <TableRow>
      <TableRowCell>
        {path}
        {accessible ? null : (
          <span className={styles.unavailable}>
            {' '}
            ({translate('Unavailable')})
          </span>
        )}
      </TableRowCell>
      <TableRowCell>{freeSpace == null ? '-' : formatBytes(freeSpace)}</TableRowCell>
      <TableRowCell>{unmappedFolders.length}</TableRowCell>
      <TableRowCell className={styles.actions}>
        <IconButton
          name={icons.REMOVE}
          title={translate('DeleteRootFolder')}
          aria-label={translate('DeleteRootFolder')}
          onPress={() => setIsDeleteOpen(true)}
        />
        <ConfirmModal
          isOpen={isDeleteOpen}
          kind={kinds.DANGER}
          title={translate('DeleteRootFolder')}
          message={translate('DeleteRootFolderMessageText', { path })}
          confirmLabel={translate('Delete')}
          isSpinning={isDeleting}
          onConfirm={() =>
            deleteProvider(undefined, {
              onSuccess: () => setIsDeleteOpen(false),
            })
          }
          onCancel={() => setIsDeleteOpen(false)}
        />
      </TableRowCell>
    </TableRow>
  );
}

function RootFolders() {
  const queryClient = useQueryClient();
  const { data, isFetching, isFetched, error } = useRootFolders();
  const [path, setPath] = useState('');

  const add = useApiMutation<RootFolder, { path: string }>({
    path: ROOT_FOLDER_PATH,
    method: 'POST',
    mutationOptions: {
      onSuccess: (added) => {
        queryClient.setQueryData<RootFolder[]>([ROOT_FOLDER_PATH], (old = []) => [
          ...old,
          added,
        ]);
        setPath('');
      },
    },
  });

  const addErrors = getValidationFailures(add.error).errors;

  return (
    <PageSectionContent
      errorMessage={translate('RootFoldersLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      {data.length ? (
        <Table columns={columns}>
          <TableBody>
            {data.map((rootFolder) => (
              <RootFolderRow key={rootFolder.id} rootFolder={rootFolder} />
            ))}
          </TableBody>
        </Table>
      ) : null}

      <div className={styles.addRootFolder}>
        <FormInput
          type={inputTypes.TEXT}
          name="rootFolderPath"
          placeholder="/data/media/sports"
          value={path}
          onChange={({ value }: InputChanged<string>) => setPath(value)}
        />
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={add.isPending}
          isDisabled={!path.trim()}
          onPress={() => add.mutate({ path: path.trim() })}
        >
          {translate('AddRootFolder')}
        </SpinnerButton>
      </div>

      {addErrors.map((e, index) => (
        <Alert key={index} kind={kinds.DANGER}>
          {e.errorMessage}
        </Alert>
      ))}
    </PageSectionContent>
  );
}

export default RootFolders;
