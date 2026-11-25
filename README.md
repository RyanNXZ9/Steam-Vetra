# CatalogEntitlementManager

Catálogo de jogos para Windows com busca, capas da Steam, biblioteca local, temas e perfil com foto. A biblioteca identifica instalações pelos arquivos locais da Steam e abre os jogos pelo cliente oficial. Remover um jogo da lista preserva a instalação.

## Edição para distribuição

Por medidas de segurança, foram retirados o módulo anti-crack, o vínculo por HWID e os componentes de geração, validação e ativação de chaves. Esta edição também exclui as integrações privadas de provisionamento, as ferramentas de modificação de DRM e a configuração de produção. Os fluxos que dependiam desses módulos estão indisponíveis.

O perfil, a foto, as preferências e a coleção são criados no computador de quem usa o aplicativo. O diretório de dados desta edição é `%LOCALAPPDATA%\CatalogEntitlementManager.Community`. As capas podem ser carregadas do CDN da Steam. Não há serviço de hospedagem de perfis.

## Executar

Em um computador Windows com o SDK .NET 8, execute na raiz do projeto:

```powershell
dotnet run --project CatalogEntitlementManager.csproj
```

