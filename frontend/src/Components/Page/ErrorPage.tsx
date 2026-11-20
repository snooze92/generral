// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Page/ErrorPage.tsx
import React from 'react';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';
import styles from './ErrorPage.module.css';

interface ErrorPageProps {
  version: string;
  isLocalStorageSupported: boolean;
  uiSettingsError: ApiError | null;
  systemStatusError: ApiError | null;
}

function getErrorMessage(error: ApiError, fallback: string) {
  return error.statusBody?.message ?? fallback;
}

function ErrorPage({
  version,
  isLocalStorageSupported,
  uiSettingsError,
  systemStatusError,
}: ErrorPageProps) {
  let errorMessage = translate('FailedToLoadApp');

  if (!isLocalStorageSupported) {
    errorMessage = translate('LocalStorageIsNotSupported');
  } else if (uiSettingsError) {
    errorMessage = getErrorMessage(
      uiSettingsError,
      translate('FailedToLoadUiSettingsFromApi')
    );
  } else if (systemStatusError) {
    errorMessage = getErrorMessage(
      systemStatusError,
      translate('FailedToLoadSystemStatusFromApi')
    );
  }

  return (
    <div className={styles.page}>
      <img
        src={`${window.Generral.urlBase}/Content/Images/logo.svg`}
        alt=""
        width={48}
        height={48}
      />

      <div>{errorMessage}</div>

      <div className={styles.version}>
        {translate('VersionNumber', { version })}
      </div>
    </div>
  );
}

export default ErrorPage;
