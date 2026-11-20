// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Page/LoadingPage.tsx
import React from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import LoadingMessage from 'Components/Loading/LoadingMessage';
import styles from './LoadingPage.module.css';

function LoadingPage() {
  return (
    <div className={styles.page}>
      <img
        className={styles.logoFull}
        src={`${window.Generral.urlBase}/Content/Images/logo.svg`}
        alt=""
      />
      <LoadingMessage />
      <LoadingIndicator />
    </div>
  );
}

export default LoadingPage;
