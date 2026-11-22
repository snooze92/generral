// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/DownloadClients/RemotePathMappings/
// RemotePathMappings.tsx, RemotePathMapping.tsx and EditRemotePathMappingModalContent.tsx (typed paths
// instead of the file browser; listed on Media Management).
import React, { useCallback, useState } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageSectionContent from 'Components/Page/PageSectionContent';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import useManageItem from 'Settings/Profiles/useManageItem';
import editProviderStyles from 'Settings/Provider/EditProviderModalContent.module.css';
import { useDeleteProvider } from 'Settings/Provider/useProviders';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { REMOTE_PATH_MAPPING_PATH, RemotePathMapping } from './MediaManagement';
import styles from './MediaManagementSettings.module.css';

const NEW_MAPPING: RemotePathMapping = {
  id: 0,
  host: '',
  remotePath: '',
  localPath: '',
};

const columns: Column[] = [
  { name: 'host', label: () => translate('Host'), isVisible: true },
  { name: 'remotePath', label: () => translate('RemotePath'), isVisible: true },
  { name: 'localPath', label: () => translate('LocalPath'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

interface EditModalProps {
  mapping: RemotePathMapping;
  isOpen: boolean;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditRemotePathMappingModal({
  mapping,
  isOpen,
  onModalClose,
  onDeletePress,
}: EditModalProps) {
  const {
    settings,
    validationErrors,
    validationWarnings,
    updateValue,
    save,
    isSaving,
    saveError,
  } = useManageItem<RemotePathMapping>(
    REMOTE_PATH_MAPPING_PATH,
    mapping,
    onModalClose
  );

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) =>
      updateValue(name as keyof RemotePathMapping, value as never),
    [updateValue]
  );

  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {mapping.id
            ? translate('EditRemotePathMapping')
            : translate('AddRemotePathMapping')}
        </ModalHeader>

        <ModalBody>
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            <FormRow>
              <FormLabel>{translate('Host')}</FormLabel>
              <FormInputHelpText
                text={translate('RemotePathMappingHostHelpText')}
              />
              <FormInput
                type={inputTypes.TEXT}
                name="host"
                {...settings.host}
                onChange={handleInputChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('RemotePath')}</FormLabel>
              <FormInputHelpText
                text={translate('RemotePathMappingRemotePathHelpText')}
              />
              <FormInput
                type={inputTypes.TEXT}
                name="remotePath"
                {...settings.remotePath}
                onChange={handleInputChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('LocalPath')}</FormLabel>
              <FormInputHelpText
                text={translate('RemotePathMappingLocalPathHelpText')}
              />
              <FormInput
                type={inputTypes.TEXT}
                name="localPath"
                {...settings.localPath}
                onChange={handleInputChange}
              />
            </FormRow>
          </Form>
        </ModalBody>

        <ModalFooter>
          {onDeletePress ? (
            <Button
              className={editProviderStyles.deleteButton}
              kind={kinds.DANGER}
              onPress={onDeletePress}
            >
              {translate('Delete')}
            </Button>
          ) : null}

          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerErrorButton
            isSpinning={isSaving}
            error={saveError}
            onPress={save}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

function RemotePathMappingRow({ mapping }: { mapping: RemotePathMapping }) {
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const { deleteProvider, isDeleting } = useDeleteProvider<RemotePathMapping>(
    REMOTE_PATH_MAPPING_PATH,
    mapping.id
  );

  return (
    <TableRow>
      <TableRowCell>{mapping.host}</TableRowCell>
      <TableRowCell>{mapping.remotePath}</TableRowCell>
      <TableRowCell>{mapping.localPath}</TableRowCell>
      <TableRowCell className={styles.actions}>
        <IconButton
          name={icons.EDIT}
          title={translate('EditRemotePathMapping')}
          aria-label={translate('EditRemotePathMapping')}
          onPress={() => setIsEditOpen(true)}
        />

        <EditRemotePathMappingModal
          mapping={mapping}
          isOpen={isEditOpen}
          onModalClose={() => setIsEditOpen(false)}
          onDeletePress={() => {
            setIsEditOpen(false);
            setIsDeleteOpen(true);
          }}
        />

        <ConfirmModal
          isOpen={isDeleteOpen}
          kind={kinds.DANGER}
          title={translate('DeleteRemotePathMapping')}
          message={translate('DeleteRemotePathMappingMessageText')}
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

function RemotePathMappings() {
  const { data = [], isFetching, isFetched, error } = useApiQuery<
    RemotePathMapping[]
  >({ path: REMOTE_PATH_MAPPING_PATH });
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <PageSectionContent
      errorMessage={translate('RemotePathMappingsLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <Table columns={columns}>
        <TableBody>
          {data.map((mapping) => (
            <RemotePathMappingRow key={mapping.id} mapping={mapping} />
          ))}
        </TableBody>
      </Table>

      <div className={styles.addMapping}>
        <IconButton
          name={icons.ADD}
          title={translate('AddRemotePathMapping')}
          aria-label={translate('AddRemotePathMapping')}
          onPress={() => setIsAddOpen(true)}
        />
      </div>

      {isAddOpen ? (
        <EditRemotePathMappingModal
          mapping={NEW_MAPPING}
          isOpen={true}
          onModalClose={() => setIsAddOpen(false)}
        />
      ) : null}
    </PageSectionContent>
  );
}

export default RemotePathMappings;
