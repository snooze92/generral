// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Status/Health/HealthStatus.tsx
import React from 'react';
import PageSidebarStatus from 'Components/Page/Sidebar/PageSidebarStatus';
import translate from 'Utilities/String/translate';
import { useHealth } from '../../useSystemData';

/** The health issue count badge next to System in the sidebar. */
function HealthStatus() {
  const { data } = useHealth();
  const count = data.length;

  return (
    <PageSidebarStatus
      aria-label={
        count === 1
          ? translate('HealthIssue')
          : translate('HealthIssues', { count })
      }
      count={count}
      errors={data.some((item) => item.type === 'error')}
      warnings={data.some((item) => item.type === 'warning')}
    />
  );
}

export default HealthStatus;
