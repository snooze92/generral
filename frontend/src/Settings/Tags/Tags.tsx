// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Tags/Tags.tsx
// Usage comes from sources and series, and tags can be added from here.
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import PageSectionContent from 'Components/Page/PageSectionContent';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { kinds } from 'Helpers/Props';
import { useSources } from 'Sources/useSources';
import useTagDetails from 'Tags/useTagDetails';
import useTags, { useSortedTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import TagDetailsModal from './Details/TagDetailsModal';
import Tag from './Tag';

function Tags() {
  const { isFetching, isFetched, error } = useTags();
  const items = useSortedTagList();
  const {
    isFetching: isDetailsFetching,
    isFetched: isDetailsFetched,
    error: detailsError,
  } = useTagDetails();
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);

  useSources();

  return (
    <PageSectionContent
      errorMessage={translate('TagsLoadError')}
      error={error || detailsError}
      isFetching={isFetching || isDetailsFetching}
      isPopulated={isFetched && isDetailsFetched}
    >
      {items.length ? null : (
        <Alert kind={kinds.INFO}>{translate('NoTagsHaveBeenAddedYet')}</Alert>
      )}

      <div className={settingsCardStyles.grid}>
        {items.map((item) => {
          return <Tag key={item.id} {...item} />;
        })}

        <AddCard
          label={translate('AddTag')}
          onPress={() => setIsAddModalOpen(true)}
        />
      </div>

      <TagDetailsModal
        isOpen={isAddModalOpen}
        onModalClose={() => setIsAddModalOpen(false)}
      />
    </PageSectionContent>
  );
}

export default Tags;
