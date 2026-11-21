import ModelBase from 'App/ModelBase';

export type CommandStatus = 'queued' | 'started' | 'completed' | 'failed';

interface Command extends ModelBase {
  name: string;
  commandName: string;
  message?: string;
  body: Record<string, unknown>;
  status: CommandStatus;
  queued: string;
  started?: string;
  ended?: string;
  trigger: 'manual' | 'scheduled';
}

export default Command;
