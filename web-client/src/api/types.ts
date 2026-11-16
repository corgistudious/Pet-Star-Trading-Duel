// Mirrors shared-contracts/Runtime/Contracts/Api.cs (camelCase JSON) plus the shapes of the expansion endpoints.
export type Currency = 'RAINBOW_STAR' | 'MINI_GREEN_BEAN';
export const CURRENCIES: Currency[] = ['RAINBOW_STAR', 'MINI_GREEN_BEAN'];

export interface AuthResponse { uid: number; accessToken: string; accessTokenExpiresAt: string; refreshToken: string; emailVerified: boolean; serverId?: number | null }
export interface ProfileDto { uid: number; email: string; emailVerified: boolean; displayName: string; language: string; avatarId: string; avatarFrameId: string; titleId?: string | null; roles: string[] }
export interface SessionDto { id: string; platform?: string; deviceName?: string; createdAt: string; lastUsedAt: string; current: boolean }
export interface ServerDto { id: number; code: string; name: string; status: string; registrationOpen: boolean; hasProgress: boolean; playerLevel?: number | null; nickname?: string | null }
export interface PlayerDto { uid: number; serverId: number; serverCode: string; nickname: string; level: number; exp: number; currencies: Record<string, number>; packs: Record<string, number>; items: Record<string, number>; cardCount: number; codexDiscovered: number; codexTotal: number }
export interface LedgerEntryDto { id: number; currency: string; delta: number; balanceAfter: number; reason: string; at: string }

export interface LimitedEventDto { id: number; nameVi: string; nameEn: string; startsAt: string; endsAt: string; totalLimit?: number | null; perPlayerLimit?: number | null; sold: number; boughtByMe: number }
export interface PackShopItemDto {
  id: string; nameKey: string; artAssetId: string; priceCurrency: Currency; priceAmount: number; owned: number; availableUntil?: string | null;
  order?: number; theme?: string; names?: Record<string, string>; descriptions?: Record<string, string>; minLevel?: number; minVip?: number;
  requiresEvent?: boolean; requiresInvitation?: boolean; canBuy?: boolean; blockedReason?: string | null; version?: number;
  event?: LimitedEventDto | null; invitationPacksLeft?: number | null; invitationExpiresAt?: string | null;
}
export interface PackOddsRow { rarity: string; perSlot: number; atLeastOneInPack: number; atLeastOneThisOrBetter: number }
export interface PackOddsDto {
  packTypeId: string; version: number; slots1To4: Record<string, number>; slot5: Record<string, number>; quality: Record<string, number>; finish: Record<string, number>; possiblePets: string[];
  sameTableAllSlots?: boolean; pack?: PackOddsRow[]; divinePets?: string[]; divinePerPetPerSlot?: number; divineGroupPerSlot?: number; divineAtLeastOneInPack?: number;
}
export interface VipDto { level: number; points: number; nextLevelPoints?: number | null; thresholds: number[] }
export interface EngravingDto { id: number; type: string; level: number; cardId?: number | null; slot?: number | null; value: number }
export interface UpgradeCostDto { beans: number; items: Record<string, number>; minLevel: number; minStars: number; minBreakthrough: number }
export interface UpgradeOptionDto { kind: string; current: number; max: number; cost?: UpgradeCostDto | null; blocked?: string | null; preview?: CardDto | null }
export interface CardUpgradesDto { card: CardDto; options: UpgradeOptionDto[]; materials: Record<string, number>; beans: number; freeEngravings: EngravingDto[]; unlockedSlots: number[] }
export interface ResetPreviewDto { kind: string; from: number; to: number; refundBeans: number; refundItems: Record<string, number>; blocked?: string | null }
export interface PurchasePackResponse { packTypeId: string; owned: number; currencies: Record<string, number> }

export interface CardDto {
  id: number; serial: string; skillLevel: number; formId: string; name: string; familyId: string; stage: number; element: string; role: string;
  rarity: string; quality: string; finish: string; level: number; exp: number; expToNext: number; hp: number; atk: number; def: number; spd: number;
  crit: number; power: number; skillId?: string | null; ultimateId?: string | null; passiveId?: string | null; isFavorite: boolean; isLocked: boolean; isBusy: boolean; slot: number;
  stars?: number; enhanceLevel?: number; breakthrough?: number; levelCap?: number; awakened?: boolean; divineSet?: string | null; engravings?: EngravingDto[];
}
export interface PackOpeningDto { openingId: string; packTypeId: string; createdAt: string; acknowledged: boolean; cards: CardDto[] }
export interface CardPageDto { items: CardDto[]; total: number; page: number; pageSize: number; copiesByForm: Record<string, number> }
export interface EvolutionPreviewDto { current: CardDto; next?: CardDto | null; check: string; requiredLevel: number; essenceItemId: string; essenceRequired: number; essenceOwned: number; beansRequired: number; beansOwned: number }
export interface CodexEntryDto { formId: string; familyId: string; stage: number; discovered: boolean; discoveredAt?: string | null; owned: number }
export interface CodexDto { discovered: number; total: number; completion: number; entries: CodexEntryDto[] }
export interface TrainingInfo { skillLevel: number; maxSkillLevel: number; next?: { scrolls: number; beans: number; minCardLevel: number } | null; scrollsOwned: number; beansOwned: number; cardLevel: number }

export interface DeckSlotDto { position: number; cardId: number }
export interface DeckDto { id: number; name: string; isDefault: boolean; slots: DeckSlotDto[]; totalPower: number }

export interface PveStageDto { id: string; chapter: number; order: number; difficulty: number; unlocked: boolean; clearCount: number; rewardedClearsLeftToday: number; enemyForms: string[] }
export type ActionType = 'Basic' | 'Skill' | 'Ultimate' | 'Defend';
export interface PlannedAction { unitIndex: number; type: ActionType; targetIndex?: number | null }
export interface BattleUnitDto {
  side: number; index: number; position: number; cardId: number; formId: string; element: string; rarity: string; level: number;
  maxHp: number; hp: number; shield: number; atk: number; def: number; spd: number; skillId?: string | null; ultimateId?: string | null; passiveId?: string | null; statuses: string[];
}
export interface BattleEvent { type: string; round: number; actorSide: number; actorIndex: number; targetSide: number; targetIndex: number; amount: number; skillId?: string | null; status?: string | null; crit: boolean; elementMultiplier: number }
export interface BattleRewardsDto { currencies: Record<string, number>; items: Record<string, number>; packs: Record<string, number>; cardExp: number; firstClear: boolean; dailyCapReached: boolean }
export interface BattleDto {
  id: string; mode: string; stageId?: string | null; round: number; myEnergy: number; opponentEnergy: number; mySide: number; status: string;
  winnerSide?: number | null; endReason?: string | null; roundDeadline?: string | null; units: BattleUnitDto[]; lastEvents: BattleEvent[]; rewards?: BattleRewardsDto | null;
}

export interface PassportValue { currency: string; initial: number; estimated: number; samples: number; confidence: string }
export interface PassportEvent { type: string; at: string; fromFormId?: string; toFormId?: string; detail?: string; currency?: string; price?: number; status?: string }
export interface PassportDto {
  serial: string; cardId?: number | null; isOwner: boolean; card: CardDto; issuedServer: string; issuedAt: string; origin: string; originRef?: string | null;
  formAtIssue: string; ownerNickname: string; ownerUid: number; transferCount: number; evolutions: PassportEvent[]; upgrades: PassportEvent[];
  listings: PassportEvent[]; trades: PassportEvent[]; values: PassportValue[]; lockStatus: string;
}

export interface ListingView { listingId: number; card: CardDto; sellerNickname: string; sellerUid: number; currency: Currency; price: number; feeBps: number; fee: number; sellerNet: number; status: string; createdAt: string; expiresAt: string; views: number; isMine: boolean; estimatedValue?: number | null }
export interface AuctionView {
  auctionId: number; card: CardDto; sellerNickname: string; currency: Currency; startPrice: number; buyoutPrice?: number | null; highestAmount?: number | null; bidCount: number;
  leading: boolean; isMine: boolean; nextMinBid?: number | null; feeBps: number; status: string; endsAt: string; createdAt: string; serverTime: string;
}

export interface FeedCardView { position: number; serial: string; snapshot: CardDto; currentOwnerNickname: string; currentOwnerUid: number; ownerChanged: boolean }
export interface FeedPostView { id: number; type: string; caption: string; authorUid: number; authorNickname: string; createdAt: string; likes: number; comments: number; likedByMe: boolean; savedByMe: boolean; featured: boolean; isMine: boolean; cards: FeedCardView[]; dataJson: string }
export interface FeedComment { id: number; text: string; createdAt: string; authorUid: number; authorNickname: string; mine: boolean }

export interface ChatMessage { id: number; text: string; createdAt: string; authorUid: number; authorNickname: string; mine: boolean; channel?: string }
export interface NotificationItem { id: number; type: string; data: Record<string, unknown>; createdAt: string; read: boolean }
export interface MailItem { id: number; titleKey: string; body?: string | null; attachments: RewardBundle | null; createdAt: string; expiresAt?: string | null; claimed: boolean }

export interface RewardBundle { currencies?: Record<string, number>; items?: Record<string, number>; packs?: Record<string, number>; cards?: { formId: string; rarity: string; quality: string; finish: string }[] }

export interface RealtimeMessage<T = unknown> { type: string; data: T; at: string }
