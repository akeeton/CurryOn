﻿namespace CurryOn.Elastic

open FSharp.Control
open Nest
open System

module Elasticsearch =
    
    /// Connects to the specified Elasticsearch Instance with the given settings and returns an IElasticClient
    let connect (settings: ElasticSettings) =
        let connectionSettings = settings.GetConnectionSettings()
        let client = ElasticClient(connectionSettings)
        { new CurryOn.Elastic.IElasticClient with
            member __.IndexExists<'index when 'index: not struct> () = Elastic.indexExists<'index> client
            member __.CreateIndex<'index when 'index: not struct> () = Elastic.createIndex<'index> client None
            member __.CreateIndex<'index when 'index: not struct> creationRequest = Elastic.createIndex<'index> client <| Some creationRequest
            member __.DeleteIndex<'index when 'index: not struct> () = Elastic.deleteIndex<'index> client
            member __.RecreateIndex<'index when 'index: not struct> () = Elastic.recreateIndex<'index> client
            member __.DeleteOldDocuments<'index when 'index: not struct> date = Elastic.deleteOldDocuments<'index> client date
            member __.Index<'index when 'index: not struct> document = Elastic.index<'index> client document
            member __.BulkIndex<'index when 'index: not struct and 'index: equality> (requests: CurryOn.Elastic.IndexRequest<'index> seq) = Elastic.bulkIndex<'index> client requests
            member __.BulkIndex<'index when 'index: not struct> (documents: 'index seq) = Elastic.bulkIndexDocuments<'index> client documents
            member __.Get<'index when 'index: not struct> request = Elastic.get<'index> client request
            member __.Delete<'index when 'index: not struct> request = Elastic.delete<'index> client request
            member __.Update<'index when 'index: not struct> request = Elastic.update<'index> client request
            member __.Search<'index when 'index: not struct> request = Elastic.search<'index> client request
            member __.Dispose () =
                (connectionSettings :> IDisposable).Dispose()
        }
        
    /// Checks to see if an index for the given type already exists in Elasticsearch
    let indexExists<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) =
        client.IndexExists<'index> ()

    /// Creates an index for the given type in the Elasticsearch repository
    let createIndex<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) =
        client.CreateIndex<'index> ()

    /// Creates an index for the given type with the specified parameters in the Elasticsearch repository
    let createIndexWithParameters<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) request =
        client.CreateIndex<'index> request

    /// Deletes the the index for the given type and all documents contained therein from Elasticsearch
    let deleteIndex<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) =
        client.DeleteIndex<'index> ()

    /// Deletes and Recreates the index for the given type in Elasticsearch
    let recreateIndex<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) = 
        client.RecreateIndex<'index> ()

    /// Deletes all documents in the index for the given type older than the specified date
    let deleteOldDocuments<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) date =
        client.DeleteOldDocuments<'index> date

    /// Indexes the given document, adding it to Elasticsearch
    let index<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) document =
        client.Index<'index> document

    /// Bulk-Indexes all given documents, adding them to Elasticsearch using the specified IDs and metadata
    let bulkIndex<'index when 'index: not struct and 'index: equality> (client: CurryOn.Elastic.IElasticClient) (requests: CurryOn.Elastic.IndexRequest<'index> seq) =
        client.BulkIndex<'index> requests

    /// Bulk-Indxes all given documents, adding them to Elasticsearch with dynamically generated IDs and metadata
    let bulkIndexDocuments<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) (documents: 'index seq) =
        client.BulkIndex<'index> documents

    /// Retrieves the document with the specified ID from the Elasticsearch index
    let get<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) request =
        client.Get<'index> request

    /// Deletes the specified document from the Elasticsearch index
    let delete<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) request =
        client.Delete<'index> request

    /// Updates the specified document in the Elasticsearch index, performing either a scripted update, field-level updates, or an Upsert
    let update<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) request =
        client.Update<'index> request

    /// Executes a search against the specified Elasticsearch index
    let search<'index when 'index: not struct> (client: CurryOn.Elastic.IElasticClient) request =
        client.Search<'index> request