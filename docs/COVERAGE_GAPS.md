# Lacunas de cobertura

Gerado por `jo sources coverage` em 05/11/2026 23:54 UTC, contra a API local. O universo é a auditoria dos 249 países e territórios ISO 3166-1
(`data/catalog/auditoria-249-paises-v2.csv`, do usuário). O estado de cada país sai só do que existe: conector do país sondado com vaga, fonte do país
no registro ainda sem conector, vaga aberta vista com local nele, ou o estado da auditoria. **Isto não é cobertura mundial completa.**

## Números desta execução

| | Quantas |
|---|---|
| Fontes configuradas (plataformas no registro) | 46 |
| Com conector no app | 43 |
| Autorizadas (portão aberto) | 39 |
| Funcionando (última sondagem com vaga) | 32 |
| Quadros de empresa na campanha | 25 |
| Empresas com vaga aberta | 88 |
| Vagas únicas abertas | 143 |
| Vagas abertas com data da fonte | 143 |
| Vagas confirmadas para quem mora no Brasil | 77 |
| Vagas possíveis para quem mora no Brasil (remotas sem restrição clara) | 46 |
| Vagas restritas (fora do alcance de quem mora no Brasil) | 6 |
| Vagas que se contradizem (revisão) | 0 |
| Vagas sem local nenhum | 0 |
| Países integrados | 4 |
| Países com vaga aberta vista | 26 |
| Países não pesquisados | 147 |
| Registros no catálogo V2 (referência, não fonte) | 321 |
| Vagas abertas sem país no local (remotas, mundo todo) | 18 |

Fontes globais ou regionais ativas (cobrem qualquer país, mas só onde as empresas anunciam): `greenhouse`, `lever`, `ashby`, `smartrecruiters`, `teamtailor`, `personio`, `breezy`, `recruitee`, `workable`, `workable-search`, `workday`, `rippling`, `oracle`, `successfactors`, `pandape`, `himalayas`, `remoteok`, `wwr`, `remotive`, `jobicy`, `getonbrd`, `arbeitnow`, `themuse`, `freehire`, `4dayweek`, `workingnomads`, `hn`, `rss`, `web`, `alertas`.

## Por continente

| Continente | Países | Integrados | Conector pendente | Vagas vistas | Pistas sem teste | Não pesquisados | Vagas abertas |
|---|---|---|---|---|---|---|---|
| Europa | 51 | 2 | 0 | 10 | 22 | 17 | 17 |
| América do Sul | 16 | 1 | 0 | 0 | 5 | 10 | 60 |
| América do Norte | 5 | 1 | 0 | 1 | 0 | 3 | 22 |
| Ásia | 51 | 0 | 0 | 10 | 14 | 27 | 21 |
| África | 60 | 0 | 0 | 1 | 25 | 34 | 5 |
| América Central e Caribe | 36 | 0 | 0 | 1 | 4 | 31 | 3 |
| Oceania | 29 | 0 | 0 | 1 | 4 | 24 | 2 |
| Antártida | 1 | 0 | 0 | 0 | 0 | 1 | 0 |

## África

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Egypt (EG) | vagas vistas por fonte global | - | - | 5 | 0/5 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Algeria (DZ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Benin (BJ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Burkina Faso (BF) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Cameroon (CM) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Central African Republic (CF) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Chad (TD) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Congo (CG) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Congo, The Democratic Republic of the (CD) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Côte d'Ivoire (CI) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Djibouti (DJ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Gabon (GA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Guinea (GN) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Kenya (KE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 3 portais, 0 APIs candidatas, 1 agências WAPES |
| Mali (ML) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Mauritania (MR) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Morocco (MA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Namibia (NA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Niger (NE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Nigeria (NG) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Rwanda (RW) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Senegal (SN) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| South Africa (ZA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 5 portais, 0 APIs candidatas, 0 agências WAPES |
| Togo (TG) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Tunisia (TN) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Zimbabwe (ZW) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |

Não pesquisados (34): Angola, Botswana, British Indian Ocean Territory, Burundi, Cabo Verde, Comoros, Equatorial Guinea, Eritrea, Eswatini, Ethiopia, French Southern Territories, Gambia, Ghana, Guinea-Bissau, Lesotho, Liberia, Libya, Madagascar, Malawi, Mauritius, Mayotte, Mozambique, Réunion, Saint Helena, Ascension and Tristan da Cunha, Sao Tome and Principe, Seychelles, Sierra Leone, Somalia, South Sudan, Sudan, Tanzania, United Republic of, Uganda, Western Sahara, Zambia.

## Antártida

Não pesquisados (1): Antarctica.

## Ásia

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Armenia (AM) | vagas vistas por fonte global | - | - | 1 | 0/1 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| India (IN) | vagas vistas por fonte global | - | - | 5 | 0/5 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Indonesia (ID) | vagas vistas por fonte global | - | - | 2 | 0/0 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Israel (IL) | vagas vistas por fonte global | - | - | 1 | 1/0 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Jordan (JO) | vagas vistas por fonte global | - | - | 3 | 0/3 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Pakistan (PK) | vagas vistas por fonte global | - | - | 5 | 1/4 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Saudi Arabia (SA) | vagas vistas por fonte global | - | - | 1 | 1/0 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Singapore (SG) | vagas vistas por fonte global | - | - | 1 | 1/0 | 2 portais, 0 APIs candidatas, 0 agências WAPES |
| United Arab Emirates (AE) | vagas vistas por fonte global | - | - | 1 | 0/1 | 8 portais, 0 APIs candidatas, 0 agências WAPES |
| Viet Nam (VN) | vagas vistas por fonte global | - | - | 1 | 0/1 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Azerbaijan (AZ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Cambodia (KH) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| China (CN) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Cyprus (CY) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Hong Kong (HK) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Japan (JP) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 2 portais, 1 APIs candidatas, 1 agências WAPES |
| Kazakhstan (KZ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Korea, Republic of (KR) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 3 portais, 2 APIs candidatas, 1 agências WAPES |
| Malaysia (MY) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Oman (OM) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Palestine, State of (PS) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Philippines (PH) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Taiwan, Province of China (TW) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Türkiye (TR) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |

Não pesquisados (27): Afghanistan, Bahrain, Bangladesh, Bhutan, Brunei Darussalam, Georgia, Iran, Islamic Republic of, Iraq, Korea, Democratic People's Republic of, Kuwait, Kyrgyzstan, Lao People's Democratic Republic, Lebanon, Macao, Maldives, Mongolia, Myanmar, Nepal, Qatar, Sri Lanka, Syrian Arab Republic, Tajikistan, Thailand, Timor-Leste, Turkmenistan, Uzbekistan, Yemen.

## América Central e Caribe

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Mexico (MX) | vagas vistas por fonte global | - | - | 3 | 1/1 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Dominican Republic (DO) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Honduras (HN) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Panama (PA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Trinidad and Tobago (TT) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |

Não pesquisados (31): Anguilla, Antigua and Barbuda, Aruba, Bahamas, Barbados, Belize, Bonaire, Sint Eustatius and Saba, Cayman Islands, Costa Rica, Cuba, Curaçao, Dominica, El Salvador, Grenada, Guadeloupe, Guatemala, Haiti, Jamaica, Martinique, Montserrat, Nicaragua, Puerto Rico, Saint Barthélemy, Saint Kitts and Nevis, Saint Lucia, Saint Martin (French part), Saint Vincent and the Grenadines, Sint Maarten (Dutch part), Turks and Caicos Islands, Virgin Islands, British, Virgin Islands, U.S..

## Europa

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Norway (NO) | integrado | `nav` | - | 0 | 0/0 | 3 portais, 2 APIs candidatas, 1 agências WAPES |
| Sweden (SE) | integrado | `jobtech`, `joblinks` | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Albania (AL) | vagas vistas por fonte global | - | - | 1 | 0/1 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| France (FR) | vagas vistas por fonte global | - | - | 2 | 0/1 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Greece (GR) | vagas vistas por fonte global | - | - | 1 | 0/1 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Italy (IT) | vagas vistas por fonte global | - | - | 3 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Latvia (LV) | vagas vistas por fonte global | - | - | 1 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Lithuania (LT) | vagas vistas por fonte global | - | - | 1 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Poland (PL) | vagas vistas por fonte global | - | - | 3 | 1/0 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| Portugal (PT) | vagas vistas por fonte global | - | - | 1 | 0/1 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Ukraine (UA) | vagas vistas por fonte global | - | - | 2 | 0/2 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| United Kingdom (GB) | vagas vistas por fonte global | - | - | 2 | 0/2 | 3 portais, 0 APIs candidatas, 1 agências WAPES |
| Austria (AT) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Belgium (BE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 4 agências WAPES |
| Bosnia and Herzegovina (BA) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Bulgaria (BG) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Croatia (HR) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Denmark (DK) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 2 portais, 1 APIs candidatas, 1 agências WAPES |
| Estonia (EE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Finland (FI) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Germany (DE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Hungary (HU) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Ireland (IE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Luxembourg (LU) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Malta (MT) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Moldova, Republic of (MD) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Montenegro (ME) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Netherlands (NL) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| North Macedonia (MK) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Romania (RO) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Serbia (RS) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Slovenia (SI) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Spain (ES) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Switzerland (CH) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |

Não pesquisados (17): Åland Islands, Andorra, Belarus, Czechia, Faroe Islands, Gibraltar, Guernsey, Holy See (Vatican City State), Iceland, Isle of Man, Jersey, Liechtenstein, Monaco, Russian Federation, San Marino, Slovakia, Svalbard and Jan Mayen.

## América do Norte

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| United States (US) | integrado | `seasonaljobs` | `usajobs` | 15 | 3/8 | 3 portais, 2 APIs candidatas, 0 agências WAPES |
| Canada (CA) | vagas vistas por fonte global | - | - | 7 | 2/5 | 2 portais, 0 APIs candidatas, 2 agências WAPES |

Não pesquisados (3): Bermuda, Greenland, Saint Pierre and Miquelon.

## Oceania

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Australia (AU) | vagas vistas por fonte global | - | - | 2 | 1/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Fiji (FJ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 0 agências WAPES |
| New Zealand (NZ) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 1 agências WAPES |
| Papua New Guinea (PG) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Samoa (WS) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 2 portais, 0 APIs candidatas, 0 agências WAPES |

Não pesquisados (24): American Samoa, Christmas Island, Cocos (Keeling) Islands, Cook Islands, French Polynesia, Guam, Heard Island and McDonald Islands, Kiribati, Marshall Islands, Micronesia, Federated States of, Nauru, New Caledonia, Niue, Norfolk Island, Northern Mariana Islands, Palau, Pitcairn, Solomon Islands, Tokelau, Tonga, Tuvalu, United States Minor Outlying Islands, Vanuatu, Wallis and Futuna.

## América do Sul

| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |
|---|---|---|---|---|---|---|
| Brazil (BR) | integrado | `gupy`, `solides`, `quickin`, `github` | `inhire` | 60 | 60/0 | 2 portais, 0 APIs candidatas, 0 agências WAPES |
| Argentina (AR) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Chile (CL) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Colombia (CO) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 1 portais, 0 APIs candidatas, 0 agências WAPES |
| Peru (PE) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |
| Uruguay (UY) | pistas sem teste ao vivo | - | - | 0 | 0/0 | 0 portais, 0 APIs candidatas, 1 agências WAPES |

Não pesquisados (10): Bolivia, Plurinational State of, Bouvet Island, Ecuador, Falkland Islands (Malvinas), French Guiana, Guyana, Paraguay, South Georgia and the South Sandwich Islands, Suriname, Venezuela, Bolivarian Republic of.

## Próximos passos

Para cada país: consultar a agência pública no diretório WAPES, achar o portal nacional com busca real, procurar API, feed, RSS ou download em lote na
documentação oficial, conferir termos, licença e dados pessoais e só então criar a fonte (no registro, com sondagem). Portal com login, CAPTCHA ou
antirrobô fica AUTH_REQUIRED ou POLICY_BLOCKED, sem contorno. A lista priorizada está em docs/NEXT_SOURCES.md.
