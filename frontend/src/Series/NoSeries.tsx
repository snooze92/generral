// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/NoSeries.tsx
import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './NoSeries.module.css';

function NoSeries() {
  return (
    <div>
      <div className={styles.message}>{translate('NoSeriesFoundAdd')}</div>

      <div className={styles.buttonContainer}>
        <Button to="/add/new" kind={kinds.PRIMARY}>
          {translate('AddNewSeries')}
        </Button>
      </div>
    </div>
  );
}

export default NoSeries;
