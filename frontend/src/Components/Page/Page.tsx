// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Page/Page.tsx
import moment from 'moment-timezone';
import React, { useCallback, useEffect } from 'react';
import { saveDimensions, useAppValue } from 'App/appStore';
import useAppPage from 'Helpers/Hooks/useAppPage';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import ErrorPage from './ErrorPage';
import PageHeader from './Header/PageHeader';
import LoadingPage from './LoadingPage';
import PageSidebar from './Sidebar/PageSidebar';
import styles from './Page.module.css';

interface PageProps {
  children: React.ReactNode;
}

function Page({ children }: PageProps) {
  const version = useAppValue('version');
  const { hasError, errors, isPopulated, isLocalStorageSupported } =
    useAppPage();

  const { timeZone } = useUiSettingsValues();

  const handleResize = useCallback(() => {
    saveDimensions({
      width: window.innerWidth,
      height: window.innerHeight,
    });
  }, []);

  useEffect(() => {
    window.addEventListener('resize', handleResize);

    return () => {
      window.removeEventListener('resize', handleResize);
    };
  }, [handleResize]);

  useEffect(() => {
    try {
      moment.tz.setDefault(timeZone);
    } catch (error) {
      console.error(
        `Error converting to timezone ${timeZone}. Using system timezone.`,
        error
      );
    }
  }, [timeZone]);

  if (hasError || !isLocalStorageSupported) {
    return (
      <ErrorPage
        {...errors}
        version={version}
        isLocalStorageSupported={isLocalStorageSupported}
      />
    );
  }

  if (!isPopulated) {
    return <LoadingPage />;
  }

  return (
    <div className={styles.page}>
      <PageHeader />

      <div className={styles.main}>
        <PageSidebar />

        {children}
      </div>
    </div>
  );
}

export default Page;
