// Generral's take on Sonarr's Wanted → Manual Import (frontend/src/InteractiveImport/Folder/InteractiveImportSelectFolderModalContent.tsx,
// GPL-3.0-or-later): a typed folder path instead of Sonarr's file browser, with an optional series to suggest episodes from.
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes, kinds } from 'Helpers/Props';
import useSeries from 'Series/useSeries';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import ManualImportModal from './ManualImportModal';

function ManualImportPage() {
  const { data: allSeries } = useSeries();
  const [folder, setFolder] = useState('');
  const [seriesId, setSeriesId] = useState(0);
  const [isOpen, setIsOpen] = useState(false);

  const handleChange = useCallback(({ name, value }: InputChanged) => {
    if (name === 'folder') {
      setFolder(String(value ?? ''));
    } else {
      setSeriesId(Number(value));
    }
  }, []);

  const path = folder.trim();

  return (
    <PageContent title={translate('ManualImport')}>
      <PageContentBody>
        <Alert kind={kinds.INFO}>{translate('ManualImportPageHelpText')}</Alert>

        <Form>
          <FormRow>
            <FormLabel>{translate('Folder')}</FormLabel>
            <FormInputHelpText text={translate('ManualImportFolderHelpText')} />
            <FormInput
              type={inputTypes.TEXT}
              name="folder"
              value={folder}
              placeholder="/data/torrents/generral/…"
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Series')}</FormLabel>
            <FormInputHelpText text={translate('ManualImportSeriesHelpText')} />
            <FormInput
              type={inputTypes.SELECT}
              name="seriesId"
              values={[
                { key: 0, value: translate('NoSeries') },
                ...allSeries.map((s) => ({ key: s.id, value: s.title })),
              ]}
              value={seriesId}
              onChange={handleChange}
            />
          </FormRow>

          <Button
            kind={kinds.PRIMARY}
            isDisabled={!path.startsWith('/') && !/^[A-Za-z]:[\\/]/.test(path)}
            onPress={() => setIsOpen(true)}
          >
            {translate('ManualImportStart')}
          </Button>
        </Form>

        <ManualImportModal
          isOpen={isOpen}
          folder={path}
          seriesId={seriesId || undefined}
          title={path}
          onModalClose={() => setIsOpen(false)}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default ManualImportPage;
