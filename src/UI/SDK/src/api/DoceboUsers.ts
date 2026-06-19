import { DoceboUserExistsResponse, DoceboUserSearchResponse } from '../models/DoceboUserSearchResponse';
import { RequiredDeep } from '../models/RequiredDeep';
import httpClient from '../utils/HttpClient';

export default class DoceboUsers {
    private impersonating:boolean = false;

    /**
    * @ignore
    * not part of public api, don't include in generated docs
    */
    constructor() {
        this.SearchUsers = this.SearchUsers.bind(this);
    }

   /**
    * @param email Email of the learner
    * @param accessToken Provide an alternative token to the one stored in the sdk instance (useful for impersonation).
    */
    public async SearchUsers(email: string,  accessToken?: string ): Promise<RequiredDeep<DoceboUserExistsResponse>> {
        const impersonating = this.impersonating;
        this.impersonating = false;
        return await httpClient.get(`/docebo/${email}`, { params: {  accessToken, impersonating } } );
    }

    /**
     * @description 
     * enables impersonation by calling the subsequent method with the stored impersonation token
     * 
     * @example
     * DoceboUsers.As().List() // lists DoceboUsers using the impersonated users' token
     */
    public As(): this {
        this.impersonating = true;
        return this;
    }
}
