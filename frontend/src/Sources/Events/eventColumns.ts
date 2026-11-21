import Column from 'Components/Table/Column';
import translate from 'Utilities/String/translate';

const eventColumns: Column[] = [
  { name: 'key', label: () => translate('Key'), isVisible: true },
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'start', label: () => translate('Start'), isVisible: true },
  { name: 'status', label: () => translate('Status'), isVisible: true },
];

export default eventColumns;
