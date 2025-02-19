import {
  Component,
  Output,
  EventEmitter,
  OnInit,
  Input,
  ChangeDetectorRef,
} from '@angular/core'
import {
  Address,
  BuyerAddress,
  BuyerCreditCard,
  ListPage,
  Me,
  OrderPromotion,
} from 'ordercloud-javascript-sdk'
import {
  HeadStartSDK,
  HSAddressBuyer,
  HSOrder,
  AssetUpload,
} from '@ordercloud/headstart-sdk'
import { groupBy as _groupBy } from 'lodash'
import { uniqBy as _uniqBy } from 'lodash'
import { CheckoutService } from 'src/app/services/order/checkout.service'
import { ShopperContextService } from 'src/app/services/shopper-context/shopper-context.service'
import {
  SelectedCreditCard,
  StripeIntent,
} from 'src/app/models/credit-card.types'
import { AcceptedPaymentTypes } from 'src/app/models/checkout.types'
import { OrderSummaryMeta } from 'src/app/models/order.types'
import { AppConfig } from 'src/app/models/environment.types'
import { faCheckCircle, faCheck } from '@fortawesome/free-solid-svg-icons'
import { getSuggestedAddresses } from 'src/app/services/address-suggestion.helper'
import { StripeConfig } from 'src/app/config/stripe.class'

@Component({
  templateUrl: './checkout-payment.component.html',
  styleUrls: ['./checkout-payment.component.scss'],
})
export class OCMCheckoutPayment implements OnInit {
  @Input() cards: ListPage<BuyerCreditCard>
  @Input() isAnon: boolean
  @Input() order: HSOrder
  @Input() paymentError: string
  @Input() orderSummaryMeta: OrderSummaryMeta
  @Output() continue = new EventEmitter<void>()
  @Output() promosChanged = new EventEmitter<OrderPromotion[]>()
  checkout: CheckoutService = this.context.order.checkout
  _orderCurrency: string
  _acceptedPaymentMethods: string[]
  selectedPaymentMethod: AcceptedPaymentTypes
  POTermsAccepted: boolean
  faCheckCircle = faCheckCircle
  faCheck = faCheck
  existingBillingAddresses: ListPage<BuyerAddress>
  selectedBillingAddress: BuyerAddress
  showNewAddressForm = false
  suggestedAddresses: BuyerAddress[]
  existingBuyerLocations: ListPage<BuyerAddress>
  selectedBuyerLocation: BuyerAddress
  homeCountry: string
  readonly NEW_ADDRESS_CODE = 'new'
  disablePO = false
  disableCC = false
  japanOrder = false
  poOnlyOrder = false
  containsSubscriptions = false
  agreedToTerms = false
  poNumber: string
  poOrderUpload: AssetUpload
  poUploadLabelText = 'Choose file'
  selectedFileName: string | null = null
  poUploadSuccess = false
  isSitecorian: boolean
  isHovered = false
  stripeCountry: EventEmitter<BuyerAddress> = new EventEmitter<BuyerAddress>()
  stripeCountries = StripeConfig.getStripeCountries()
  stripeKeyMap = StripeConfig.getStripeKeyMap()

  constructor(
    private context: ShopperContextService,
    private appConfig: AppConfig,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this._orderCurrency = this.context.currentUser.get().Currency
    this._acceptedPaymentMethods = this.getAcceptedPaymentMethods()
    this.isSitecorian = this.isSitecoreEmail()
    const _order = this.context.order.get()

    if (_order.Total > 0 && _order?.xp?.ShippingAddress?.Country != 'JP') {
      this.selectedPaymentMethod = this
        ._acceptedPaymentMethods?.[0] as AcceptedPaymentTypes
    } else {
      this.selectedPaymentMethod = this
        ._acceptedPaymentMethods?.[1] as AcceptedPaymentTypes
      this.disableCC = true
    }
    this.ListAddressesForBilling()
    const lineItems = this.context.order.getLineItems()
    lineItems.Items.forEach((line) => {
      if (line?.Product?.xp?.isSubscription && line.UnitPrice > 0) {
        this.containsSubscriptions = true
      }
    })

    if (_order?.xp?.PONumber) {
      this.poNumber = _order.xp.PONumber
    }
  }

  OnMouseEnter(): void {
    this.isHovered = true
  }

  OnMouseLeave(): void {
    this.isHovered = false
  }

  get buttonText(): string {
    if (this.isHovered) {
      return this.poUploadSuccess ? 'File uploaded' : 'No file uploaded'
    } else {
      return 'Upload PDF'
    }
  }


  getAcceptedPaymentMethods(): string[] {
    if (
      this.appConfig?.acceptedPaymentMethods == null ||
      this.appConfig?.acceptedPaymentMethods?.length < 1
    ) {
      return [AcceptedPaymentTypes.CreditCard]
    }
    return this.appConfig.acceptedPaymentMethods
  }

  selectPaymentMethod(e: any): void {
    this.selectedPaymentMethod = e.target.value
    this.selectedBillingAddress = null
  }

  getPaymentMethodName(method: string): string {
    return method.split(/(?=[A-Z])/).join(' ')
  }

  poChanged(e): void {
    this.poNumber = e.target.value
  }

  showNewAddress(): void {
    this.showNewAddressForm = true
  }

  addressFormChanged(address: BuyerAddress): void {
    this.selectedBillingAddress = address
  }

  handleFormDismissed(): void {
    this.showNewAddressForm = false
  }

  onBillingAddressChange(billingAddressID: string): void {
    this.stripeCountry.emit(null)
    this.japanOrder = false
    this.poOnlyOrder = false
    this.showNewAddressForm = billingAddressID === this.NEW_ADDRESS_CODE
    this.selectedBillingAddress = this.existingBillingAddresses.Items.find(
      (address) => billingAddressID === address.ID
    )
    const billingAddress = this.existingBillingAddresses.Items.find(
      (address) => address.ID === this.selectedBillingAddress?.ID
    )
    if (billingAddress) {
      this.selectedBillingAddress = billingAddress
      const billingAddressCopy = { ...this.selectedBillingAddress }
      this.context.order.checkout.setOneTimeAddress(
        billingAddressCopy as Address,
        'billing'
      )
      const _order = this.context.order.get()
      const keyName =
        this.appConfig.sellerName == 'Sitecore LMS TEST'
          ? 'TestPublishableKey'
          : 'ProductionPublishableKey'

      const stripeAccount = this.stripeCountries.find(
        (country) => country.Code == this.selectedBillingAddress.Country
      )
      const stripeInfo = this.stripeKeyMap.find(
        (key) => key.StripeAccount == stripeAccount.StripeAccount
      )
      const stripeKey = stripeInfo[keyName]

      if (this.selectedBillingAddress.Country == 'JP') {
        this.selectedPaymentMethod = this
          ._acceptedPaymentMethods?.[1] as AcceptedPaymentTypes
        this.japanOrder = true
        this.disableCC = true
      } else if (stripeKey == 'PO_ONLY') {
        this.selectedPaymentMethod = this
          ._acceptedPaymentMethods?.[1] as AcceptedPaymentTypes
        this.poOnlyOrder = true
        this.disableCC = true
      } else {
        if (_order.Total > 0 ) {
          this.disableCC = false
          this.stripeCountry.emit(this.selectedBillingAddress)
        } else {
          this.selectedPaymentMethod = this
            ._acceptedPaymentMethods?.[1] as AcceptedPaymentTypes
          this.disableCC = true
        }
      }
    }
  }

  isSitecoreEmail(): any {
    const user = this.context.currentUser.get()
    if (user.Email) {
      const domain = user.Email.split('@')[1]
      return domain === 'sitecore.com' || domain === 'sitecore.net'
    }
    return
  }

  async saveAddressesAndContinue(address: BuyerAddress): Promise<void> {
    await this.context.order.checkout.setOneTimeAddress(
      address as Address,
      'billing'
    )
    await this.saveNewBillingAddress(address)
    this.showNewAddressForm = false
    this.ListAddressesForBilling()
  }

  private async ListAddressesForBilling() {
    const buyerLocationsFilter = {
      filters: { Editable: 'false' },
    }
    const billingAddressesFilter = {
      filters: { Billing: 'true', Editable: 'true' },
    }
    const [buyerLocations, existingBillingAddresses] = await Promise.all([
      Me.ListAddresses(buyerLocationsFilter),
      HeadStartSDK.Services.ListAll(
        Me,
        Me.ListAddresses,
        billingAddressesFilter
      ),
    ])
    this.homeCountry = buyerLocations?.Items[0]?.Country || 'US'
    this.existingBillingAddresses = existingBillingAddresses
  }

  private async saveNewBillingAddress(
    address: BuyerAddress
  ): Promise<HSAddressBuyer> {
    address.Shipping = false
    address.Billing = true
    try {
      const savedAddress = await this.context.addresses.create(address)
      return savedAddress
    } catch (ex) {
      return this.handleAddressError(ex)
    }
  }

  private handleAddressError(ex: any): null {
    this.suggestedAddresses = getSuggestedAddresses(ex)
    if (!(this.suggestedAddresses?.length >= 1)) throw ex
    return null // set this.selectedShippingAddress
  }
  // used when no selection of card is required
  // only acknowledgement of purchase order is required
  async onContinue(): Promise<void> {
    if (this.selectedBillingAddress) {
      if (this.poNumber) {
        const currentOrder = this.context.order.get()
        currentOrder.xp.PONumber = this.poNumber
        await this.context.order.patch(currentOrder)
      }
      this.context.order.checkout.setOneTimeAddress(
        this.selectedBillingAddress as Address,
        'billing'
      )
      this.continue.emit()
    }
  }

  //PDF upload enhancement
  onFileSelected(event: any): void {
    let asset: AssetUpload = {}
    if (
      event.target.files[0] !== null &&
      event.target.files[0] !== undefined &&
      !Array.isArray(event) &&
      event.target.files[0].type === 'application/pdf'
    ) {
      asset = {
        Active: true,
        Title: 'document',
        File: event.target.files[0],
        FileName: event.target.files[0].name,
      } as AssetUpload
      this.poOrderUpload = asset
      this.selectedFileName = asset.FileName
      this.poUploadLabelText = this.selectedFileName
        ? this.selectedFileName
        : 'Choose file'
    } else {
      alert('must upload .pdf')
    }
  }

  async uploadPdf(): Promise<void> {
    if (this.poOrderUpload) {
      try {
        const results = await HeadStartSDK.Upload.UploadPO({
          File: this.poOrderUpload.File,
          Filename: this.poOrderUpload.FileName,
        })
        if (results && results.Url && results.FileName) {
          this.poUploadSuccess = true
          this.cdr.detectChanges()
          //set the fileName on order to pull file on orderSumbit for email sending
          const currentOrder = this.context.order.get()
          currentOrder.xp.POFileID = this.GetAssetIDFromUr(results.Url)
          await this.context.order.patch(currentOrder)
        }
      } catch (error) {
        console.error('error:', error)
      }
    } else {
      alert('No file selected')
    }
  }

  //HELPER
  GetAssetIDFromUr(url: string): string {
    const parts = url.split('/')
    return parts[parts.length - 1]
  }
}
