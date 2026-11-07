import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Cloud, Cpu, Settings2 } from 'lucide-react'
import { api, type AgentName, type AgentProviderSetting, type ModelProvider } from '../api'
import { AgentIcon, Badge, ErrorMessage, Loading, PageHeader, SectionTitle } from '../components'

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
    <section className="page narrow">
      <div>
        <PageHeader icon={Settings2} title="Settings" />
        <SectionTitle icon={Cpu}>Agent providers</SectionTitle>
        <p className="page-description">
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
      </div>
    </section>
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
    <li className="card provider">
      <AgentIcon agent={setting.agent} />
      <div className="provider-name">
        <strong>
          {setting.agent}
          {setting.provider !== setting.defaultProvider && (
            <Badge tone="pending">default {providerLabel(setting.defaultProvider)}</Badge>
          )}
        </strong>
        {current?.model && <code>{current.model}</code>}
      </div>
      <div className="segmented" role="group" aria-label={`${setting.agent}'s provider`}>
        {setting.choices.map((choice) => (
          <button
            key={choice.provider}
            aria-pressed={choice.provider === setting.provider}
            disabled={busy || choice.provider === setting.provider || choice.unavailable !== null}
            title={choice.unavailable ?? choice.model}
            onClick={() => onChoose(choice.provider)}
          >
            {choice.provider === 'OpenAICompatible' ? <Cpu aria-hidden /> : <Cloud aria-hidden />}
            {providerLabel(choice.provider)}
          </button>
        ))}
      </div>
    </li>
  )
}

function providerLabel(provider: ModelProvider) {
  return provider === 'OpenAICompatible' ? 'Local model' : 'Anthropic'
}
