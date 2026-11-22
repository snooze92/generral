// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Quality/QualitySettings.tsx
// Number inputs instead of Sonarr's size sliders; all definitions are saved in one PUT /update.
import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import TextInput from 'Components/Form/TextInput';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageSectionContent from 'Components/Page/PageSectionContent';
import SectionHeading from 'Components/SectionHeading';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiMutation, {
  getValidationFailures,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import translate from 'Utilities/String/translate';

export interface QualityDefinition {
  id: number;
  quality: { id: number; name: string };
  title: string;
  weight: number;
  minSize: number | null;
  maxSize: number | null;
  preferredSize: number | null;
}

type SizeKey = 'minSize' | 'preferredSize' | 'maxSize';

const PATH = '/qualitydefinition';
const NONE: QualityDefinition[] = [];

const columns: Column[] = [
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'minSize', label: () => translate('MinimumMegabytesPerMinute'), isVisible: true },
  {
    name: 'preferredSize',
    label: () => translate('PreferredMegabytesPerMinute'),
    isVisible: true,
  },
  { name: 'maxSize', label: () => translate('MaximumMegabytesPerMinute'), isVisible: true },
];

function QualitySettings() {
  const queryClient = useQueryClient();
  const { data, isFetching, isFetched, error } = useApiQuery<
    QualityDefinition[]
  >({ path: PATH });
  const definitions = (data ?? NONE) as QualityDefinition[];
  const [pending, setPending] = useState<
    Record<number, Partial<QualityDefinition>>
  >({});

  const items = useMemo(
    () => definitions.map((d) => ({ ...d, ...pending[d.id] })),
    [definitions, pending]
  );

  const {
    mutate: save,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<QualityDefinition[], QualityDefinition[]>({
    path: `${PATH}/update`,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (saved) => {
        queryClient.setQueryData([PATH], saved);
        setPending({});
      },
    },
  });

  const handleChange = useCallback(
    (id: number, key: 'title' | SizeKey, value: string | number | null) => {
      setPending((state) => ({ ...state, [id]: { ...state[id], [key]: value } }));
    },
    []
  );

  const handleSavePress = useCallback(() => save(items), [items, save]);

  const { errors } = getValidationFailures(saveError);

  return (
    <SettingsPage
      title={translate('QualitySettings')}
      hasPendingChanges={Object.keys(pending).length > 0}
      isSaving={isSaving}
      onSavePress={handleSavePress}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Quality')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('QualityDefinitions')}
              description={translate('QualityDefinitionsSizeHelpText')}
            />

            {errors.map((e, index) => (
              <Alert key={index} kind={kinds.DANGER}>
                {e.errorMessage}
              </Alert>
            ))}

            <PageSectionContent
              errorMessage={translate('QualityDefinitionsLoadError')}
              error={error}
              isFetching={isFetching}
              isPopulated={isFetched}
            >
              <Table columns={columns}>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.id}>
                      <TableRowCell>{item.quality.name}</TableRowCell>

                      <TableRowCell>
                        <TextInput
                          name={`${item.id}.title`}
                          value={item.title}
                          onChange={({ value }) =>
                            handleChange(item.id, 'title', value)
                          }
                        />
                      </TableRowCell>

                      {(['minSize', 'preferredSize', 'maxSize'] as const).map(
                        (key) => (
                          <TableRowCell key={key}>
                            <NumberInput
                              name={`${item.id}.${key}`}
                              value={item[key]}
                              isFloat={true}
                              min={0}
                              placeholder={
                                key === 'minSize' ? '0' : translate('Unlimited')
                              }
                              onChange={({ value }: NumberInputChanged) =>
                                handleChange(item.id, key, value)
                              }
                            />
                          </TableRowCell>
                        )
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </PageSectionContent>
          </div>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default QualitySettings;
