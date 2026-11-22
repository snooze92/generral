import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import { PendingSection } from 'typings/pending';
import selectSettings from 'Utilities/selectSettings';

/** The pending state of a new (from `initial`) or existing item of the list at `path`. */
const useManageItem = <T extends ModelBase>(
  path: string,
  item: T | undefined,
  onSaved: () => void
) => {
  const queryClient = useQueryClient();
  const { pendingChanges, setPendingChange } = usePendingChangesStore<T>({});

  const {
    mutate,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<T, T>({
    path: item?.id ? `${path}/${item.id}` : path,
    method: item?.id ? 'PUT' : 'POST',
    mutationOptions: {
      onSuccess: (saved) => {
        queryClient.setQueryData<T[]>([path], (oldData = []) =>
          addOrUpdateQueryClientItem(oldData, saved, 'id')
        );
        onSaved();
      },
    },
  });

  const { settings, validationErrors, validationWarnings } = useMemo(
    () => selectSettings(item ?? {}, pendingChanges, saveError),
    [item, pendingChanges, saveError]
  );

  const current = useMemo(
    () => (item ? { ...item, ...pendingChanges } : undefined),
    [item, pendingChanges]
  );

  const save = useCallback(() => {
    if (current) {
      mutate(current);
    }
  }, [current, mutate]);

  return {
    current,
    settings: settings as PendingSection<T>,
    validationErrors,
    validationWarnings,
    updateValue: setPendingChange,
    save,
    isSaving,
    saveError,
  };
};

export default useManageItem;
