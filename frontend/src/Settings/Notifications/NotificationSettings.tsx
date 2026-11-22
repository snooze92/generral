// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Notifications/NotificationSettings.tsx
// and Notifications/Notifications.tsx, Notification.tsx; with a Plex token help box.
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageSectionContent from 'Components/Page/PageSectionContent';
import SectionHeading from 'Components/SectionHeading';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { kinds, sizes } from 'Helpers/Props';
import ProviderCard from 'Settings/Provider/ProviderCard';
import { useProviders } from 'Settings/Provider/useProviders';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import translate from 'Utilities/String/translate';
import EditNotificationModal from './EditNotificationModal';
import Notification, { PATH } from './Notification';

function NotificationCard({ notification }: { notification: Notification }) {
  const { id, name } = notification;
  const triggers = [
    notification.onGrab && translate('OnGrab'),
    notification.onDownload && translate('OnFileImport'),
    notification.onUpgrade && translate('OnFileUpgrade'),
    notification.onSeriesDelete && translate('OnSeriesDelete'),
    notification.onEpisodeFileDelete && translate('OnEpisodeFileDelete'),
    notification.onHealthIssue && translate('OnHealthIssue'),
  ].filter((t): t is string => !!t);

  return (
    <ProviderCard
      path={PATH}
      id={id}
      name={name}
      editLabel={translate('EditConnectionImplementation', {
        implementationName: notification.implementationName,
      })}
      deleteTitle={translate('DeleteNotification')}
      deleteMessage={translate('DeleteNotificationMessageText', { name })}
      tags={notification.tags}
      labels={
        triggers.length ? (
          triggers.map((trigger) => (
            <Label key={trigger} kind={kinds.SUCCESS} size={sizes.MEDIUM}>
              {trigger}
            </Label>
          ))
        ) : (
          <Label kind={kinds.DISABLED} outline={true} size={sizes.MEDIUM}>
            {translate('Disabled')}
          </Label>
        )
      }
      renderModal={(modalProps) => (
        <EditNotificationModal id={id} {...modalProps} />
      )}
    />
  );
}

function NotificationSettings() {
  const { data, isFetching, isFetched, error } =
    useProviders<Notification>(PATH);
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <SettingsPage title={translate('ConnectSettings')} showSave={false}>
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Connect')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading title={translate('Connections')} />

            <Alert kind={kinds.INFO}>{translate('PlexTokenHelpText')}</Alert>

            <PageSectionContent
              errorMessage={translate('NotificationsLoadError')}
              error={error}
              isFetching={isFetching}
              isPopulated={isFetched}
            >
              <div className={settingsCardStyles.grid}>
                {data.map((notification) => (
                  <NotificationCard
                    key={notification.id}
                    notification={notification}
                  />
                ))}

                <AddCard
                  label={translate('AddConnection')}
                  onPress={() => setIsAddOpen(true)}
                />
              </div>

              <EditNotificationModal
                isOpen={isAddOpen}
                onModalClose={() => setIsAddOpen(false)}
              />
            </PageSectionContent>
          </div>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default NotificationSettings;
