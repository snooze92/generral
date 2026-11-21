import React, { useState } from 'react';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds, sizes } from 'Helpers/Props';
import Source from 'Sources/Source';
import { useSourceEvents } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import eventColumns from './eventColumns';
import EventsTableRow from './EventsTableRow';
import styles from './SourceEventsModal.module.css';

interface SourceEventsModalContentProps {
  source: Source;
  onModalClose: () => void;
}

function SourceEventsModalContent({
  source,
  onModalClose,
}: SourceEventsModalContentProps) {
  const [includeGone, setIncludeGone] = useState(false);
  const { data, isLoading, error } = useSourceEvents(source.id, includeGone);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={source.name}>{translate('Events')}</ModalHeader>

      <ModalBody>
        <label className={styles.toggle}>
          <CheckInput
            name="includeGone"
            value={includeGone}
            onChange={({ value }) => setIncludeGone(value)}
          />
          {translate('ShowGoneEvents')}
        </label>

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('ErrorLoadingContent')}</Alert>
        ) : null}

        {!isLoading && !error && !data.length ? (
          <Alert kind={kinds.INFO}>{translate('NoEvents')}</Alert>
        ) : null}

        {data.length ? (
          <Table columns={eventColumns}>
            <TableBody>
              {data.map((event) => (
                <EventsTableRow key={event.id} event={event} />
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

interface SourceEventsModalProps extends SourceEventsModalContentProps {
  isOpen: boolean;
}

function SourceEventsModal({
  isOpen,
  onModalClose,
  ...otherProps
}: SourceEventsModalProps) {
  return (
    <Modal size={sizes.LARGE} isOpen={isOpen} onModalClose={onModalClose}>
      <SourceEventsModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default SourceEventsModal;
