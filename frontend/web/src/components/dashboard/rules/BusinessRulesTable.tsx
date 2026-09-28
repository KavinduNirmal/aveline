import { useState } from 'react'
import { Plus, RefreshCw, SlidersHorizontal } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  createBusinessRule,
  deleteBusinessRule,
  updateBusinessRule,
  type BusinessRuleResponseDto,
  type CreateBusinessRuleDto,
  type UpdateBusinessRuleDto,
} from '@/lib/business-rules-api'
import { toApiError } from '@/lib/api-error'

interface BusinessRulesTableProps {
  organizationId: string
  rules: BusinessRuleResponseDto[]
  isLoading: boolean
  canManage: boolean
  onReload: () => Promise<void>
}

// The evaluator (`BusinessRulesService.EvaluateOrderRulesAsync`) matches a **closed** set of
// `ruleType` values, lower-cased: `approval_threshold`/`high_value`, `min_margin`/`margin`,
// `discount`/`loyalty_tier`. It also parses `ruleValue` as a JSON object and reads a named
// property. A rule created with another string, or with a bare number, is stored and listed
// but never applied.
const RULE_TYPES = [
  { value: 'approval_threshold', label: 'High Value Threshold', key: 'threshold', hint: 'e.g. 40000' },
  { value: 'min_margin', label: 'Minimum Profit Margin', key: 'min_margin', hint: 'e.g. 0.25 for 25%' },
  { value: 'discount', label: 'Discount Limit', key: 'max_discount', hint: 'e.g. 0.15 for 15%' },
] as const

const RULE_TYPE_LABELS: Record<string, string> = {
  approval_threshold: 'High Value Threshold',
  high_value: 'High Value Threshold',
  min_margin: 'Minimum Profit Margin',
  margin: 'Minimum Profit Margin',
  discount: 'Discount Limit',
  loyalty_tier: 'Discount Limit',
}

/** The JSON property the evaluator reads for a given rule type. */
function ruleValueKey(ruleType: string): string {
  switch (ruleType.toLowerCase()) {
    case 'min_margin':
    case 'margin':
      return 'min_margin'
    case 'discount':
    case 'loyalty_tier':
      return 'max_discount'
    default:
      return 'threshold'
  }
}

/** The value hint for a rule type, tolerating the evaluator's aliases. */
function ruleValueHint(ruleType: string): string {
  const canonical = ruleValueKey(ruleType)
  switch (canonical) {
    case 'min_margin':
      return 'e.g. 0.25 for 25%'
    case 'max_discount':
      return 'e.g. 0.15 for 15%'
    default:
      return 'e.g. 40000'
  }
}

/** Builds the `{"<key>": <number>}` payload the evaluator expects from the operator's input. */
function buildRuleValue(ruleType: string, raw: string): string {
  const trimmed = raw.trim()
  if (trimmed === '') return raw
  // The dialog's own placeholder shows "LKR 40,000"; strip separators so it is not posted as
  // non-JSON and rejected with "Invalid JSON format for RuleValue".
  const numeric = Number(trimmed.replace(/[,\s]/g, ''))
  if (!Number.isFinite(numeric)) return raw
  return JSON.stringify({ [ruleValueKey(ruleType)]: numeric })
}

/** Reads the operator-facing number back out of a stored rule payload for the edit dialog. */
function readRuleValue(ruleValue: string, ruleType: string): string {
  try {
    const parsed = JSON.parse(ruleValue) as Record<string, unknown> | null
    const value = parsed?.[ruleValueKey(ruleType)]
    return value === undefined || value === null ? ruleValue : String(value)
  } catch {
    return ruleValue
  }
}

export function BusinessRulesTable({
  organizationId,
  rules,
  isLoading,
  canManage,
  onReload,
}: BusinessRulesTableProps) {
  const [targetRule, setTargetRule] = useState<BusinessRuleResponseDto | null>(null)
  const [ruleValueDraft, setRuleValueDraft] = useState('')
  const [ruleDescriptionDraft, setRuleDescriptionDraft] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)

  // Create rule state
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [newRuleName, setNewRuleName] = useState('')
  const [newRuleType, setNewRuleType] = useState('approval_threshold')
  const [newRuleValue, setNewRuleValue] = useState('')
  const [newRuleDescription, setNewRuleDescription] = useState('')
  const [isCreating, setIsCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  const handleToggleActive = async (rule: BusinessRuleResponseDto, nextState: boolean) => {
    if (!canManage) return
    try {
      await updateBusinessRule(organizationId, rule.id, { isActive: nextState })
      toast.success(`Rule "${rule.ruleName}" ${nextState ? 'enabled' : 'disabled'}.`)
      await onReload()
    } catch (err) {
      toast.error(toApiError(err).message || 'Failed to update rule status.')
    }
  }

  const openEditModal = (rule: BusinessRuleResponseDto) => {
    setTargetRule(rule)
    setRuleValueDraft(readRuleValue(rule.ruleValue, rule.ruleType))
    setRuleDescriptionDraft(rule.description ?? '')
    setSaveError(null)
  }

  const handleSaveEdit = async () => {
    if (!targetRule) return
    setIsSaving(true)
    setSaveError(null)
    try {
      const payload: UpdateBusinessRuleDto = {
        ruleValue: buildRuleValue(targetRule.ruleType, ruleValueDraft.trim()),
        description: ruleDescriptionDraft.trim() || undefined,
      }
      await updateBusinessRule(organizationId, targetRule.id, payload)
      toast.success(`Rule "${targetRule.ruleName}" updated successfully.`)
      setTargetRule(null)
      await onReload()
    } catch (err) {
      setSaveError(toApiError(err).message || 'Failed to update rule.')
    } finally {
      setIsSaving(false)
    }
  }

  const handleCreateRule = async () => {
    if (!newRuleName.trim() || !newRuleValue.trim()) {
      setCreateError('Rule name and threshold value are required.')
      return
    }
    setIsCreating(true)
    setCreateError(null)
    try {
      const dto: CreateBusinessRuleDto = {
        ruleName: newRuleName.trim(),
        ruleType: newRuleType,
        ruleValue: buildRuleValue(newRuleType, newRuleValue.trim()),
        description: newRuleDescription.trim() || undefined,
        isActive: true,
      }
      await createBusinessRule(organizationId, dto)
      toast.success(`Rule "${dto.ruleName}" created.`)
      setCreateDialogOpen(false)
      setNewRuleName('')
      setNewRuleValue('')
      setNewRuleDescription('')
      await onReload()
    } catch (err) {
      setCreateError(toApiError(err).message || 'Failed to create business rule.')
    } finally {
      setIsCreating(false)
    }
  }

  const handleDeleteRule = async (ruleId: string, ruleName: string) => {
    if (!canManage) return
    try {
      await deleteBusinessRule(organizationId, ruleId)
      toast.success(`Rule "${ruleName}" deleted.`)
      await onReload()
    } catch (err) {
      toast.error(toApiError(err).message || 'Failed to delete rule.')
    }
  }

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <CardTitle className="font-serif text-lg font-medium">Business Rules & Thresholds</CardTitle>
            <CardDescription>
              Configure policy thresholds that trigger the Human-in-the-Loop approval queue.
            </CardDescription>
          </div>
          {canManage && (
            <Button
              type="button"
              size="sm"
              className="gap-1.5"
              onClick={() => setCreateDialogOpen(true)}
            >
              <Plus className="size-4" aria-hidden /> Add Rule
            </Button>
          )}
        </div>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <Skeleton className="h-44 w-full" />
        ) : rules.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <SlidersHorizontal className="size-8 text-muted-foreground/50 mb-2" />
            <p className="text-sm text-muted-foreground">
              No business rules configured yet.
            </p>
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Rule Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Threshold / Value</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Active</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rules.map((rule) => (
                <TableRow key={rule.id}>
                  <TableCell className="font-medium">{rule.ruleName}</TableCell>
                  <TableCell>
                    <Badge variant="outline">
                      {RULE_TYPE_LABELS[rule.ruleType] ?? rule.ruleType}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <span className="font-mono text-xs bg-muted px-2 py-0.5 rounded">
                      {rule.ruleValue}
                    </span>
                  </TableCell>
                  <TableCell className="max-w-xs text-xs text-muted-foreground truncate">
                    {rule.description || '—'}
                  </TableCell>
                  <TableCell>
                    <Switch
                      checked={rule.isActive}
                      disabled={!canManage}
                      onCheckedChange={(checked) => handleToggleActive(rule, checked)}
                      aria-label={`Toggle ${rule.ruleName}`}
                    />
                  </TableCell>
                  <TableCell className="text-right">
                    {canManage ? (
                      <div className="flex items-center justify-end gap-2">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => openEditModal(rule)}
                        >
                          Edit
                        </Button>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="text-destructive hover:bg-destructive/10"
                          onClick={() => handleDeleteRule(rule.id, rule.ruleName)}
                        >
                          Delete
                        </Button>
                      </div>
                    ) : (
                      <span className="text-xs text-muted-foreground">View only</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>

      {/* Edit Rule Dialog */}
      <Dialog open={targetRule !== null} onOpenChange={(open) => !open && setTargetRule(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Edit Rule: {targetRule?.ruleName}</DialogTitle>
            <DialogDescription>
              Adjust threshold criteria and operational notes for this policy rule.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4 py-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="edit-rule-value">Threshold / Value</Label>
              <Input
                id="edit-rule-value"
                value={ruleValueDraft}
                onChange={(e) => setRuleValueDraft(e.target.value)}
                placeholder={ruleValueHint(targetRule?.ruleType ?? '')}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="edit-rule-desc">Description</Label>
              <Input
                id="edit-rule-desc"
                value={ruleDescriptionDraft}
                onChange={(e) => setRuleDescriptionDraft(e.target.value)}
                placeholder="Rule intent and purpose"
              />
            </div>
            {saveError && <p className="text-sm text-destructive">{saveError}</p>}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setTargetRule(null)}
              disabled={isSaving}
            >
              Cancel
            </Button>
            <Button type="button" onClick={() => void handleSaveEdit()} disabled={isSaving}>
              {isSaving && <RefreshCw className="size-4 animate-spin mr-1.5" aria-hidden />}
              Save Changes
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Create Rule Dialog */}
      <Dialog open={createDialogOpen} onOpenChange={setCreateDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Create Business Rule</DialogTitle>
            <DialogDescription>
              Define a new threshold to route commerce orders into the approval queue.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4 py-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-rule-name">Rule Name</Label>
              <Input
                id="create-rule-name"
                value={newRuleName}
                onChange={(e) => setNewRuleName(e.target.value)}
                placeholder="e.g. High Value Order Threshold"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-rule-type">Rule Type</Label>
              <Select value={newRuleType} onValueChange={setNewRuleType}>
                <SelectTrigger id="create-rule-type" aria-label="Select rule type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {RULE_TYPES.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-rule-value">Value / Limit</Label>
              <Input
                id="create-rule-value"
                value={newRuleValue}
                onChange={(e) => setNewRuleValue(e.target.value)}
                placeholder={ruleValueHint(newRuleType)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-rule-description">Description (Optional)</Label>
              <Input
                id="create-rule-description"
                value={newRuleDescription}
                onChange={(e) => setNewRuleDescription(e.target.value)}
                placeholder="e.g. Orders exceeding LKR 40,000 require manager approval"
              />
            </div>
            {createError && <p className="text-sm text-destructive">{createError}</p>}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setCreateDialogOpen(false)}
              disabled={isCreating}
            >
              Cancel
            </Button>
            <Button type="button" onClick={() => void handleCreateRule()} disabled={isCreating}>
              {isCreating && <RefreshCw className="size-4 animate-spin mr-1.5" aria-hidden />}
              Create Rule
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  )
}
