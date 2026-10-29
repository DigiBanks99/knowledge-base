import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type AgentName, type AgentProviderSetting, type ModelProvider } from '../api'
import { Badge, ErrorMessage, Loading } from '../components'

export function SettingsPage() {
  const queryClient = useQueryClient()
  const providers = useQuery({ queryKey: ['agent-providers'], queryFn: api.agentProviders })
  const setProvider = useMutation({
    mutationFn: ({ agent, provider }: { agent: AgentName; provider: ModelProvider }) =>
      api.setAgentProvider(agent, provider),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['agent-providers'] }),
        queryClient.invalidateQueries({ queryKey: ['status'] }),
      ]),
  })

  return (
    <>
      <h1>Settings</h1>
      <h2>Agent providers</h2>
      <p className="muted">
        Which model each agent runs on. A switch applies from the agent's next request and is kept across restarts;
        choosing the default again follows the deployment's setting.
      </p>

      {setProvider.isError && <ErrorMessage error={setProvider.error} />}
      {providers.isPending && <Loading what="the agents' providers" />}
      {providers.isError && <ErrorMessage error={providers.error} />}
      {providers.data && (
        <ul className="agent-providers">
          {providers.data.map((setting) => (
            <AgentProviderItem
              key={setting.agent}
              setting={setting}
              busy={setProvider.isPending && setProvider.variables?.agent === setting.agent}
              onChoose={(provider) => setProvider.mutate({ agent: setting.agent, provider })}
            />
          ))}
        </ul>
      )}
    </>
  )
}

function AgentProviderItem({
  setting,
  busy,
  onChoose,
}: {
  setting: AgentProviderSetting
  busy: boolean
  onChoose: (provider: ModelProvider) => void
}) {
  const current = setting.choices.find((choice) => choice.provider === setting.provider)

  return (
    <li className="card">
      <div className="item-title">
        <strong>{setting.agent}</strong>
        <span className="muted small">{current?.model}</span>
        {setting.provider !== setting.defaultProvider && (
          <Badge tone="pending">default {providerLabel(setting.defaultProvider)}</Badge>
        )}
        <span className="actions">
          {setting.choices.map((choice) => (
            <button
              key={choice.provider}
              className={choice.provider === setting.provider ? undefined : 'secondary'}
              aria-pressed={choice.provider === setting.provider}
              disabled={busy || choice.provider === setting.provider || choice.unavailable !== null}
              title={choice.unavailable ?? choice.model}
              onClick={() => onChoose(choice.provider)}
            >
              {providerLabel(choice.provider)}
            </button>
          ))}
        </span>
      </div>
    </li>
  )
}

function providerLabel(provider: ModelProvider) {
  return provider === 'OpenAICompatible' ? 'Local model' : 'Anthropic'
}
